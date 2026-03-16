using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Navigation;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Infrastructure.Agent.Handlers;

internal sealed class UnderstandProjectsHandler(CodeUnderstandingQueryService queries, IAnalysisService analysisService)
{
    private const int DeepHotspotCount = 10;

    public async Task<UnderstandProjectsResult> HandleAsync(UnderstandProjectsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profile = request.Profile.NormalizeProfile();

        var (solution, error) = await queries.GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before understanding projects.",
            null,
            ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new UnderstandProjectsResult(
                profile,
                [],
                [],
                AgentErrorInfo.Normalize(error, "Call load_solution first to select a solution before understanding projects."));
        }

        var includeTypes = profile is "standard" or "deep";
        var projects = await BuildProjectSummariesAsync(solution, includeTypes, ct).ConfigureAwait(false);

        if (profile != "deep")
        {
            return new UnderstandProjectsResult(profile, projects, [], null);
        }

        var metricResult = await analysisService.GetCodeMetricsAsync(new GetCodeMetricsRequest(), ct).ConfigureAwait(false);
        var hotspots = await queries.BuildHotspotsAsync(solution, metricResult.Metrics, DeepHotspotCount, ct).ConfigureAwait(false);

        return new UnderstandProjectsResult(
            profile,
            projects,
            hotspots,
            AgentErrorInfo.Normalize(metricResult.Error, "Run understand_projects again after diagnostics/metrics collection succeeds."));
    }

    private static async Task<ProjectLandscapeSummary[]> BuildProjectSummariesAsync(Solution solution, bool includeTypes, CancellationToken ct)
    {
        var outgoingByPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var incomingByPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in solution.Projects)
        {
            var projectPath = GetProjectPath(project);
            outgoingByPath.TryAdd(projectPath, []);
            incomingByPath.TryAdd(projectPath, []);
        }

        foreach (var project in solution.Projects)
        {
            var sourcePath = GetProjectPath(project);

            foreach (var reference in project.ProjectReferences)
            {
                var dependencyProject = solution.GetProject(reference.ProjectId);
                if (dependencyProject == null)
                {
                    continue;
                }

                var targetPath = GetProjectPath(dependencyProject);
                outgoingByPath[sourcePath].Add(targetPath);
                incomingByPath[targetPath].Add(sourcePath);
            }
        }

        var summaries = new List<ProjectLandscapeSummary>();
        foreach (var project in solution.Projects)
        {
            var projectPath = GetProjectPath(project);
            var types = includeTypes
                ? await BuildProjectTypesAsync(project, ct).ConfigureAwait(false)
                : [];

            summaries.Add(new ProjectLandscapeSummary(
                project.Name,
                project.FilePath,
                outgoingByPath[projectPath].OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
                incomingByPath[projectPath].OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
                types));
        }

        return summaries
            .OrderByDescending(static project => project.OutgoingDependencyProjectPaths.Count + project.IncomingDependencyProjectPaths.Count)
            .ThenBy(static project => project.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<IReadOnlyList<string>> BuildProjectTypesAsync(Project project, CancellationToken ct)
    {
        var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
        if (compilation == null)
        {
            return [];
        }

        var visibleTypes = new List<string>();
        var generatedFallbackTypes = new List<string>();

        foreach (var type in compilation.Assembly.GlobalNamespace.EnumerateTypes())
        {
            if (!type.Locations.Any(static location => location.IsInSource) || type.ToTypeKind() == null)
            {
                continue;
            }

            var compactType = $"{type.CreateId().ToExternal()}: {type.ToQualifiedDisplayName()}";
            var (filePath, _, _) = type.GetDeclarationPosition();

            if (SourceVisibility.ShouldIncludeInHumanResults(filePath))
            {
                visibleTypes.Add(compactType);
                continue;
            }

            generatedFallbackTypes.Add(compactType);
        }

        var selectedTypes = visibleTypes.Count > 0 ? visibleTypes : generatedFallbackTypes;
        return selectedTypes
            .OrderBy(static type => type, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GetProjectPath(Project project)
        => project.FilePath ?? string.Empty;
}
