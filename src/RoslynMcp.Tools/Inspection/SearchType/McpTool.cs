using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.SearchType;

public sealed record Match(string FullName, string ProjectPath);

public sealed record Result(
    IReadOnlyList<Match> Matches,
    Inspection.LoadType.Result? Type = null,
    bool? Truncated = null,
    ErrorInfo? Error = null)
{
    public static Result AsError(string message, IReadOnlyDictionary<string, string>? details = null)
        => new([], null, null, new ErrorInfo(message, details));
}

[McpServerToolType]
public sealed class McpTool(
    WorkspaceManager workspaceManager,
    SolutionManager solutionManager,
    SymbolManager symbolManager,
    Inspection.LoadType.McpTool loadTypeTool)
    : Tool
{
    [McpServerTool(Name = "search_type", Title = "Search Type", ReadOnly = true, Idempotent = true)]
    [Description(
        "Use this tool when you only have a type name fragment. It performs a fast syntax-only search over the loaded solution. " +
        "If exactly one match is found, it automatically returns the load_type result for that type.")]
    public async Task<Result> Execute(
        CancellationToken cancellationToken,
        [Description("Type name fragment to search for (case-insensitive contains match).")]
        string? query = null)
    {
        if (solutionManager.Solution is not { } solution)
            return Result.AsError("load solution first");

        if (string.IsNullOrWhiteSpace(query))
            return Result.AsError("query is required");

        query = query.Trim();

        var candidates = await FindCandidatesAsync(solution, query, cancellationToken).ConfigureAwait(false);

        if (candidates.Count == 0)
            return NoTypeFound(query);

        candidates = SelectCandidates(candidates);

        return candidates.Count == 1
            ? await LoadUniqueType(solution, candidates[0], cancellationToken).ConfigureAwait(false)
            : BuildAmbiguousResult(candidates);
    }

    private static Result NoTypeFound(string query)
        => Result.AsError("no type found", new Dictionary<string, string> { ["query"] = query });

    private static List<FoundMatch> SelectCandidates(List<FoundMatch> candidates)
        => CollapseRepeatedDeclarations(PreferHandwritten(candidates));

    private static List<FoundMatch> PreferHandwritten(List<FoundMatch> candidates)
    {
        if (!candidates.Any(m => m.IsHandwritten))
            return candidates;

        return candidates.Where(m => m.IsHandwritten).ToList();
    }

    private static List<FoundMatch> CollapseRepeatedDeclarations(List<FoundMatch> candidates)
        => candidates
            .DistinctBy(m => (m.FullName, m.ProjectPath))
            .ToList();

    private Result BuildAmbiguousResult(List<FoundMatch> candidates)
    {
        const int maxMatches = 50;

        var ordered = candidates
            .OrderBy(m => m.FullName, StringComparer.Ordinal)
            .ThenBy(m => m.ProjectPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var truncated = ordered.Count > maxMatches;
        if (truncated)
            ordered = ordered.Take(maxMatches).ToList();

        var matches = ordered
            .Select(m => new Match(
                m.FullName,
                workspaceManager.ToRelativePathIfPossible(m.ProjectPath) ?? m.ProjectPath))
            .ToList();

        return truncated
            ? new Result(matches, Truncated: true)
            : new Result(matches);
    }

    private async Task<Result> LoadUniqueType(Solution solution, FoundMatch match, CancellationToken cancellationToken)
    {
        var document = solution.GetDocument(match.DocumentId);

        if (document is null)
            return Result.AsError("document not found");

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return Result.AsError("no syntax root");

        var node = root.FindNode(match.Span, getInnermostNodeForTie: true);

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return Result.AsError("no semantic model");

        var symbol = TryResolveNamedTypeSymbol(node, semanticModel, cancellationToken);
        if (symbol is null)
            return Result.AsError("type symbol not found");

        var symbolId = symbolManager.ToId(symbol);

        var typeResult = await loadTypeTool.Execute(cancellationToken, typeSymbolId: symbolId).ConfigureAwait(false);
        return new Result([], typeResult);
    }

    private sealed record FoundMatch(
        string FullName,
        string ProjectPath,
        DocumentId DocumentId,
        TextSpan Span,
        bool IsHandwritten);

    private static INamedTypeSymbol? TryResolveNamedTypeSymbol(SyntaxNode node, SemanticModel semanticModel, CancellationToken ct)
    {
        var enumDecl = node.FirstAncestorOrSelf<EnumDeclarationSyntax>();
        if (enumDecl is not null)
            return semanticModel.GetDeclaredSymbol(enumDecl, ct) as INamedTypeSymbol;

        var delegateDecl = node.FirstAncestorOrSelf<DelegateDeclarationSyntax>();
        if (delegateDecl is not null)
            return semanticModel.GetDeclaredSymbol(delegateDecl, ct) as INamedTypeSymbol;

        var typeDecl = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (typeDecl is not null)
            return semanticModel.GetDeclaredSymbol(typeDecl, ct) as INamedTypeSymbol;

        return null;
    }

    private static async Task<List<FoundMatch>> FindCandidatesAsync(Solution solution, string query, CancellationToken ct)
    {
        var matches = new List<FoundMatch>();

        foreach (var project in solution.Projects)
        {
            if (project.FilePath is not { } projectPath || string.IsNullOrWhiteSpace(projectPath))
                continue;

            foreach (var document in project.Documents)
            {
                ct.ThrowIfCancellationRequested();

                if (!document.SupportsSyntaxTree)
                    continue;

                if (document.FilePath is null)
                    continue;

                var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                if (root is null)
                    continue;

                foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                    AddMatch(matches, query, projectPath, document, typeDecl.Identifier.ValueText, typeDecl.Identifier.Span, typeDecl.GetNamespaceName(), typeDecl.GetContainingTypeLikeChain(), typeDecl.TypeParameterList?.Parameters.Count ?? 0);

                foreach (var enumDecl in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
                    AddMatch(matches, query, projectPath, document, enumDecl.Identifier.ValueText, enumDecl.Identifier.Span, enumDecl.GetNamespaceName(), enumDecl.GetContainingTypeLikeChain(), genericArity: 0);

                foreach (var delDecl in root.DescendantNodes().OfType<DelegateDeclarationSyntax>())
                    AddMatch(matches, query, projectPath, document, delDecl.Identifier.ValueText, delDecl.Identifier.Span, delDecl.GetNamespaceName(), delDecl.GetContainingTypeLikeChain(), delDecl.TypeParameterList?.Parameters.Count ?? 0);
            }
        }

        return matches;
    }

    private static void AddMatch(
        List<FoundMatch> matches,
        string query,
        string projectPath,
        Document document,
        string name,
        TextSpan span,
        string ns,
        string container,
        int genericArity)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
            return;

		var identity = SyntaxNamingExtensions.BuildQualifiedTypeIdentity(container, name, genericArity);
		var fullName = string.IsNullOrWhiteSpace(ns) ? identity : $"{ns}.{identity}";

        matches.Add(new FoundMatch(
            fullName,
            projectPath,
            document.Id,
            span,
            document.FilePath.IsHandwritten()));
    }
}
