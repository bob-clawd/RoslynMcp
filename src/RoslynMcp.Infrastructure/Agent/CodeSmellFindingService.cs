using RoslynMcp.Core.Contracts;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Analysis;
using RoslynMcp.Infrastructure.Navigation;
using RoslynMcp.Infrastructure.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace RoslynMcp.Infrastructure.Agent;

public sealed class CodeSmellFindingService : ICodeSmellFindingService
{
    private const int MaximumScannedAnchors = 500;

    private readonly IRoslynSolutionAccessor _solutionAccessor;
    private readonly IRefactoringService _refactoringService;
    private readonly RoslynatorAnalyzerCatalog _analyzerCatalog;

    public CodeSmellFindingService(IRoslynSolutionAccessor solutionAccessor, IRefactoringService refactoringService)
    {
        _solutionAccessor = solutionAccessor ?? throw new ArgumentNullException(nameof(solutionAccessor));
        _refactoringService = refactoringService ?? throw new ArgumentNullException(nameof(refactoringService));
        _analyzerCatalog = new RoslynatorAnalyzerCatalog();
    }

    public async Task<FindCodeSmellsResult> FindCodeSmellsAsync(FindCodeSmellsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = NormalizePath(request.Path);

        if (path is null)
        {
            return CreateInvalidInputResult("path is required.", ("field", "path"));
        }

        var (solution, solutionError) = await _solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
        if (solution is null)
        {
            return new FindCodeSmellsResult(
                Array.Empty<CodeSmellMatch>(),
                Array.Empty<string>(),
                AgentErrorInfo.Normalize(solutionError,
                    "Call load_solution first to select a solution before finding code smells."));
        }

        var matchingDocuments = solution.Projects
            .SelectMany(static project => project.Documents)
            .Where(candidate => candidate.FilePath.MatchesByNormalizedPath(path))
            .OrderBy(static candidate => candidate.FilePath, StringComparer.Ordinal)
            .ToArray();

        if (matchingDocuments.Length == 0)
        {
            var error = AgentErrorInfo.Create(
                ErrorCodes.InvalidPath,
                "path did not match any loaded document.",
                "Use a source document path that exists in the loaded solution.",
                ("field", "path"),
                ("provided", path));
            return new FindCodeSmellsResult(Array.Empty<CodeSmellMatch>(), Array.Empty<string>(), error);
        }

        if (matchingDocuments.Length > 1)
        {
            var error = AgentErrorInfo.Create(
                ErrorCodes.InvalidPath,
                "path matched multiple loaded documents.",
                "Provide a unique source document path from the loaded solution.",
                ("field", "path"),
                ("provided", path));
            return new FindCodeSmellsResult(Array.Empty<CodeSmellMatch>(), Array.Empty<string>(), error);
        }

        var document = matchingDocuments[0];

        var warnings = new List<string>();
        var anchors = await CollectAnchorsAsync(document, warnings, ct).ConfigureAwait(false);
        var actions = new List<RankedAction>();

        foreach (var anchor in anchors)
        {
            var discovered = await _refactoringService.GetRefactoringsAtPositionAsync(
                new GetRefactoringsAtPositionRequest(anchor.FilePath, anchor.Line, anchor.Column, null, null, "default"),
                ct).ConfigureAwait(false);

            if (discovered.Error is not null)
            {
                warnings.Add($"Skipped position {anchor.FilePath}:{anchor.Line}:{anchor.Column} ({anchor.AnchorKind}): {discovered.Error.Message}");
                continue;
            }

            var actionsAtAnchor = discovered.Actions
                .OrderBy(action => string.Equals(action.PolicyDecision.Decision, "allow", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(action => MapRisk(action.RiskLevel))
                .ThenBy(action => action.Title, StringComparer.Ordinal)
                .ThenBy(action => action.ActionId, StringComparer.Ordinal)
                .ToArray();

            // If no refactorings found but this is a diagnostic anchor, still create an action
            if (actionsAtAnchor.Length == 0 && anchor.IsDiagnostic && anchor.AnchorKind.StartsWith("Diagnostic:"))
            {
                var diagnosticId = anchor.AnchorKind.Substring("Diagnostic:".Length);
                actionsAtAnchor = new[]
                {
                    new RefactoringActionDescriptor(
                        $"diagnostic_{diagnosticId}",
                        $"Diagnostic: {diagnosticId}",
                        "analyzer",
                        "roslynator_diagnostic",
                        "info",
                        new PolicyDecisionInfo("allow", "diagnostic", "Found by analyzer"),
                        new SourceLocation(anchor.FilePath, anchor.Line, anchor.Column),
                        diagnosticId)
                };
            }

            if (actionsAtAnchor.Length == 0)
            {
                continue;
            }

            foreach (var action in actionsAtAnchor)
            {
                actions.Add(new RankedAction(
                    new CodeSmellMatch(
                        action.Title,
                        action.Category,
                        new SourceLocation(anchor.FilePath, anchor.Line, anchor.Column),
                        action.Origin,
                        action.RiskLevel),
                    anchor.IsDiagnostic ? 0 : 1,
                    string.Equals(action.PolicyDecision.Decision, "allow", StringComparison.Ordinal) ? 0 : 1,
                    MapRisk(action.RiskLevel)));
            }
        }

        var orderedActions = actions
            .OrderBy(static action => action.AnchorPriority)
            .ThenBy(static action => action.PolicyPriority)
            .ThenBy(static action => action.RiskPriority)
            .ThenBy(static action => action.Action.Location.FilePath, StringComparer.Ordinal)
            .ThenBy(static action => action.Action.Location.Line)
            .ThenBy(static action => action.Action.Location.Column)
            .ThenBy(static action => action.Action.Title, StringComparer.Ordinal)
            .ThenBy(static action => action.Action.Category, StringComparer.Ordinal)
            .ThenBy(static action => action.Action.Origin, StringComparer.Ordinal)
            .ThenBy(static action => action.Action.RiskLevel, StringComparer.Ordinal)
            .Select(static action => action.Action)
            .ToArray();

        return new FindCodeSmellsResult(orderedActions, warnings);
    }

    private async Task<IReadOnlyList<AnchorPosition>> CollectAnchorsAsync(Document document, List<string> warnings, CancellationToken ct)
    {
        var anchors = new List<AnchorPosition>();

        if (document.SupportsSyntaxTree)
        {
            var syntaxRoot = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (syntaxRoot is null)
            {
                warnings.Add($"Skipped document without syntax root: {document.FilePath ?? document.Name}");
            }
            else
            {
                anchors.AddRange(EnumerateDeclarationAnchors(syntaxRoot, document.FilePath));
            }
        }

        anchors.AddRange(await EnumerateDiagnosticAnchorsAsync(document, ct).ConfigureAwait(false));

        var ordered = anchors
            .OrderBy(static anchor => anchor.FilePath, StringComparer.Ordinal)
            .ThenBy(static anchor => anchor.Line)
            .ThenBy(static anchor => anchor.Column)
            .ThenByDescending(static anchor => anchor.IsDiagnostic)
            .ThenBy(static anchor => anchor.AnchorKind, StringComparer.Ordinal)
            .ToArray();

        var deduplicated = new List<AnchorPosition>(ordered.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var anchor in ordered)
        {
            var key = CreateAnchorDeduplicationKey(anchor.FilePath, anchor.Line, anchor.Column);
            if (!seen.Add(key))
            {
                continue;
            }

            deduplicated.Add(anchor);
        }

        if (deduplicated.Count <= MaximumScannedAnchors)
        {
            return deduplicated;
        }

        warnings.Add($"Anchor discovery truncated to {MaximumScannedAnchors} positions from {deduplicated.Count}.");
        return deduplicated.Take(MaximumScannedAnchors).ToArray();
    }

    private static IEnumerable<AnchorPosition> EnumerateDeclarationAnchors(SyntaxNode syntaxRoot, string? filePath)
    {
        var normalizedPath = filePath ?? string.Empty;

        // Collect anchors at ALL syntax nodes, not just member declarations
        // This allows finding refactorings at any position in the code
        foreach (var node in syntaxRoot.DescendantNodes())
        {
            var lineSpan = node.GetLocation().GetLineSpan();
            var start = lineSpan.StartLinePosition;
            var anchorPath = string.IsNullOrWhiteSpace(lineSpan.Path) ? normalizedPath : lineSpan.Path;
            if (string.IsNullOrWhiteSpace(anchorPath))
            {
                continue;
            }

            yield return new AnchorPosition(
                anchorPath,
                start.Line + 1,
                start.Character + 1,
                node.GetType().Name,
                IsDiagnostic: false);
        }
    }

    private async Task<IReadOnlyList<AnchorPosition>> EnumerateDiagnosticAnchorsAsync(Document document, CancellationToken ct)
    {
        var compilation = await document.Project.GetCompilationAsync(ct).ConfigureAwait(false);
        if (compilation is null)
        {
            return Array.Empty<AnchorPosition>();
        }

        var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
        if (tree is null)
        {
            return Array.Empty<AnchorPosition>();
        }

        var anchors = new List<AnchorPosition>();

        // Get compiler diagnostics
        var compilerAnchors = GetCompilerDiagnostics(compilation, tree);
        anchors.AddRange(compilerAnchors);

        // Get Roslynator analyzer diagnostics
        var analyzerAnchors = await GetRoslynatorDiagnosticsAsync(compilation, tree, ct).ConfigureAwait(false);
        anchors.AddRange(analyzerAnchors);

        return anchors;
    }

    private static IEnumerable<AnchorPosition> GetCompilerDiagnostics(Compilation compilation, SyntaxTree tree)
    {
        var anchors = new List<AnchorPosition>();

        foreach (var diagnostic in compilation.GetDiagnostics())
        {
            if (!diagnostic.Location.IsInSource || diagnostic.Location.SourceTree is null)
            {
                continue;
            }

            if (!ReferenceEquals(diagnostic.Location.SourceTree, tree))
            {
                continue;
            }

            anchors.Add(CreateDiagnosticAnchor(diagnostic));
        }

        return anchors;
    }

    private async Task<IReadOnlyList<AnchorPosition>> GetRoslynatorDiagnosticsAsync(Compilation compilation, SyntaxTree tree, CancellationToken ct)
    {
        var anchors = new List<AnchorPosition>();

        var (analyzers, analyzerError) = _analyzerCatalog.GetCatalog();
        if (analyzerError is not null || analyzers.IsDefaultOrEmpty)
        {
            return anchors;
        }

        try
        {
            var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers);
            var allDiagnostics = await compilationWithAnalyzers.GetAllDiagnosticsAsync(ct).ConfigureAwait(false);
            var analyzerDiagnostics = allDiagnostics.Where(d => d.Location.IsInSource && d.Location.SourceTree == tree);

            foreach (var diagnostic in analyzerDiagnostics)
            {
                anchors.Add(CreateDiagnosticAnchor(diagnostic));
            }
        }
        catch (Exception)
        {
            // If analyzer diagnostics fail, just continue with compiler diagnostics
        }

        return anchors;
    }

    private static AnchorPosition CreateDiagnosticAnchor(Diagnostic diagnostic)
    {
        var lineSpan = diagnostic.Location.GetLineSpan();
        var start = lineSpan.StartLinePosition;
        var anchorPath = string.IsNullOrWhiteSpace(lineSpan.Path)
            ? diagnostic.Location.SourceTree?.FilePath
            : lineSpan.Path;

        return new AnchorPosition(
            anchorPath ?? string.Empty,
            start.Line + 1,
            start.Character + 1,
            $"Diagnostic:{diagnostic.Id}",
            IsDiagnostic: true);
    }

    private static FindCodeSmellsResult CreateInvalidInputResult(string message, params (string Key, string? Value)[] details)
        => new(Array.Empty<CodeSmellMatch>(), Array.Empty<string>(), AgentErrorInfo.Create(ErrorCodes.InvalidInput, message, "Adjust input and retry find_codesmells.", details));

    private static string? NormalizePath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    private static string CreateAnchorDeduplicationKey(string path, int line, int column)
        => string.Join('|', NormalizePathForDeduplication(path), line, column);

    private static string NormalizePathForDeduplication(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }

    private static RiskLevel MapRisk(string riskLevel)
    {
        return riskLevel.ToLowerInvariant() switch
        {
            "safe" or "low" => RiskLevel.Low,
            "review_required" or "medium" => RiskLevel.Medium,
            _ => RiskLevel.High
        };
    }

    private sealed record AnchorPosition(string FilePath, int Line, int Column, string AnchorKind, bool IsDiagnostic);

    private sealed record RankedAction(
        CodeSmellMatch Action,
        int AnchorPriority,
        int PolicyPriority,
        RiskLevel RiskPriority);
}
