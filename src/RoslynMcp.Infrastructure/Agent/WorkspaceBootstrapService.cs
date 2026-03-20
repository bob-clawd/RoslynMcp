using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Workspace;

namespace RoslynMcp.Infrastructure.Agent;

/// <summary>
/// Bootstraps workspace: discovers and loads solutions by path or directory scan.
/// Handles MSBuild registration and solution loading with readiness checks.
/// </summary>
public sealed class WorkspaceBootstrapService(
    ISolutionSessionService solutionSessionService,
    IAnalysisService analysisService,
    IRoslynSolutionAccessor solutionAccessor,
    ICurrentWorkspaceRootProvider currentWorkspaceRootProvider)
    : IWorkspaceBootstrapService
{
    private static readonly WorkspaceReadiness DefaultReadiness = new(WorkspaceReadinessStates.Ready, Array.Empty<string>());
    private readonly string _workspaceRoot = currentWorkspaceRootProvider?.WorkspaceRoot ?? throw new ArgumentNullException(nameof(currentWorkspaceRootProvider));

    public async Task<LoadSolutionResult> LoadSolutionAsync(LoadSolutionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var hint = request.WithWorkspaceAbsolutePaths(_workspaceRoot).SolutionHintPath?.Trim();
        var (solutionPath, discoveryError) = await ResolveSolutionPathAsync(hint, ct).ConfigureAwait(false);
        if (solutionPath == null)
        {
            return new LoadSolutionResult(
                null,
                string.Empty,
                string.Empty,
                Array.Empty<ProjectSummary>(),
                new DiagnosticsSummary(0, 0, 0, 0),
                DefaultReadiness,
                AgentErrorInfo.Normalize(discoveryError, "Provide a valid solution path or run load_solution from a folder containing a .sln or .slnx file."))
                .WithWorkspaceRelativePaths(_workspaceRoot);
        }

        var select = await solutionSessionService.SelectSolutionAsync(new SelectSolutionRequest(solutionPath), ct).ConfigureAwait(false);
        if (select.Error != null)
        {
            return new LoadSolutionResult(
                null,
                string.Empty,
                string.Empty,
                [],
                new DiagnosticsSummary(0, 0, 0, 0),
                DefaultReadiness,
                AgentErrorInfo.Normalize(select.Error, "Provide a valid .sln or .slnx path and retry load_solution."))
                .WithWorkspaceRelativePaths(_workspaceRoot);
        }

        var (solution, currentError) = await solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new LoadSolutionResult(
                select.SelectedSolutionPath,
                string.Empty,
                string.Empty,
                [],
                new DiagnosticsSummary(0, 0, 0, 0),
                DefaultReadiness,
                AgentErrorInfo.Normalize(currentError, "Retry load_solution after the workspace/session is available."));
        }

        var projects = solution.Projects
            .OrderBy(static p => p.Name, StringComparer.Ordinal)
            .Select(static p => new ProjectSummary(p.Name, p.FilePath))
            .ToArray();

        var baseline = await analysisService.AnalyzeScopeAsync(new AnalyzeScopeRequest(AnalysisScopes.Solution), ct).ConfigureAwait(false);
        var diagnostics = baseline.Diagnostics.ToLoadBaselineDiagnosticsSummary();
        var readiness = AssessReadiness(solution, baseline.Diagnostics);

        var (workspaceVersion, versionError) = await solutionAccessor.GetWorkspaceVersionAsync(ct).ConfigureAwait(false);
        if (versionError != null)
        {
            return new LoadSolutionResult(select.SelectedSolutionPath,
                string.Empty,
                string.Empty,
                projects,
                diagnostics,
                readiness,
                AgentErrorInfo.Normalize(versionError, "Retry load_solution to refresh workspace snapshot metadata."))
                .WithWorkspaceRelativePaths(_workspaceRoot);
        }

        var workspaceId = select.SelectedSolutionPath ?? string.Empty;
        var snapshotId = workspaceVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new LoadSolutionResult(select.SelectedSolutionPath, workspaceId, snapshotId, projects, diagnostics, readiness)
            .WithWorkspaceRelativePaths(_workspaceRoot);
    }

    private static WorkspaceReadiness AssessReadiness(Microsoft.CodeAnalysis.Solution solution, IReadOnlyList<DiagnosticItem> diagnostics)
    {
        var missingDocuments = solution.Projects
            .SelectMany(static project => project.Documents)
            .Where(static document => !string.IsNullOrWhiteSpace(document.FilePath))
            .Where(static document => !File.Exists(document.FilePath!))
            .ToArray();

        var missingGeneratedDocuments = missingDocuments
            .Where(static document => SourceVisibility.IsGeneratedLike(document.FilePath))
            .ToArray();

        if (missingDocuments.Length > 0)
        {
            var degradedReasons = new List<string>();

            if (missingGeneratedDocuments.Length > 0)
                degradedReasons.Add("missing_artifacts");

            if (missingDocuments.Length > missingGeneratedDocuments.Length)
                degradedReasons.Add("missing_documents");

            return new WorkspaceReadiness(
                WorkspaceReadinessStates.DegradedMissingArtifacts,
                degradedReasons,
                "Run dotnet restore/build to regenerate missing artifacts, then reload the solution if discovery looks incomplete.");
        }

        var generatedDiagnostics = diagnostics.Count(static diagnostic =>
            SourceVisibility.IsGeneratedLike(diagnostic.Location.FilePath)
            && !string.Equals(diagnostic.Severity, "info", StringComparison.OrdinalIgnoreCase));
        if (generatedDiagnostics > 0)
        {
            return new WorkspaceReadiness(
                WorkspaceReadinessStates.DegradedRestoreRecommended,
                ["generated_or_intermediate_diagnostics"],
                "Navigation may still work, but running dotnet restore/build should improve workspace completeness.");
        }

        return DefaultReadiness;
    }

    private async Task<(string? Path, ErrorInfo? Error)> ResolveSolutionPathAsync(string? hint, CancellationToken ct)
    {
        if (IsExplicitSolutionPath(hint))
            return (hint, null);

        var root = string.IsNullOrWhiteSpace(hint) ? _workspaceRoot : hint;
        var discovered = await solutionSessionService.DiscoverSolutionsAsync(new DiscoverSolutionsRequest(root), ct).ConfigureAwait(false);
        if (discovered.Error != null)
        {
            return (null, discovered.Error);
        }

        if (discovered.SolutionPaths.Count == 0)
        {
            return (null, AgentErrorInfo.Create(
                "solution_not_found",
                "No solution files were discovered.",
                "Provide a solution hint path or run load_solution from a workspace that contains a .sln or .slnx file."));
        }

        return (discovered.SolutionPaths[0], null);
    }

    private static bool IsExplicitSolutionPath(string? hint) => !string.IsNullOrWhiteSpace(hint) &&
        (hint.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || hint.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase));
}
