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
    ErrorInfo? Error = null)
{
    public static Result AsError(string message, IReadOnlyDictionary<string, string>? details = null)
        => new([], null, new ErrorInfo(message, details));
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

        var candidates = await FindMatchesAsync(solution, query, cancellationToken).ConfigureAwait(false);

        if (candidates.Count == 0)
            return Result.AsError("no type found", new Dictionary<string, string> { ["query"] = query });

        // Prefer handwritten matches when available.
        if (candidates.Any(m => m.IsHandwritten))
            candidates.RemoveAll(m => !m.IsHandwritten);

        // If ambiguous: keep output small.
        if (candidates.Count != 1)
        {
            return new Result(
                candidates
                    .Select(m => new Match(
                        m.FullName,
                        workspaceManager.ToRelativePathIfPossible(m.ProjectPath) ?? m.ProjectPath))
                    .DistinctBy(m => (m.FullName, m.ProjectPath))
                    .OrderBy(m => m.FullName, StringComparer.Ordinal)
                    .ThenBy(m => m.ProjectPath, StringComparer.OrdinalIgnoreCase)
                    .ToList());
        }

        // If exactly one match: resolve to a stable symbol id and immediately return load_type.
        var match = candidates[0];
        var document = solution.GetDocument(match.DocumentId);

        if (document is null)
            return Result.AsError("document not found");

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return Result.AsError("no syntax root");

        var node = root.FindNode(match.Span, getInnermostNodeForTie: true);

        var typeDecl = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (typeDecl is null)
            return Result.AsError("type declaration not found");

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return Result.AsError("no semantic model");

        var symbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken) as INamedTypeSymbol;
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

    private static async Task<List<FoundMatch>> FindMatchesAsync(Solution solution, string query, CancellationToken ct)
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
                {
                    var name = typeDecl.Identifier.ValueText;
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var ns = GetNamespace(typeDecl);
                    var fullName = string.IsNullOrWhiteSpace(ns) ? name : $"{ns}.{name}";

                    matches.Add(new FoundMatch(
                        fullName,
                        projectPath,
                        document.Id,
                        typeDecl.Identifier.Span,
                        document.FilePath.IsHandwritten()));
                }
            }
        }

        return matches;
    }

    private static string GetNamespace(SyntaxNode node)
    {
        for (SyntaxNode? current = node; current is not null; current = current.Parent)
        {
            if (current is BaseNamespaceDeclarationSyntax ns)
                return ns.Name.ToString();
        }

        return string.Empty;
    }
}
