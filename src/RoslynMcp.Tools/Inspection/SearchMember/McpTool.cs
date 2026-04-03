using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.SearchMember;

public sealed record Match(string FullName, string ProjectPath, string? Location, string? Kind);

public sealed record Result(
    IReadOnlyList<Match> Matches,
    Inspection.LoadMember.Result? Member = null,
    bool Truncated = false,
    ErrorInfo? Error = null)
{
    public static Result AsError(string message, IReadOnlyDictionary<string, string>? details = null)
        => new([], null, false, new ErrorInfo(message, details));
}

[McpServerToolType]
public sealed class McpTool(
    WorkspaceManager workspaceManager,
    SolutionManager solutionManager,
    SymbolManager symbolManager,
    Inspection.LoadMember.McpTool loadMemberTool)
    : Tool
{
    [McpServerTool(Name = "search_member", Title = "Search Member", ReadOnly = true, Idempotent = true)]
    [Description(
        "Use this tool when you only have a member name fragment (method/property/event/ctor). " +
        "It performs a fast syntax-only search over the loaded solution. " +
        "If exactly one match is found, it automatically returns the load_member result for that symbol.")]
    public async Task<Result> Execute(
        CancellationToken cancellationToken,
        [Description("Member name fragment to search for (case-insensitive contains match).")]
        string? query = null)
    {
        if (solutionManager.Solution is not { } solution)
            return Result.AsError("load solution first");

        if (string.IsNullOrWhiteSpace(query))
            return Result.AsError("query is required");

        query = query.Trim();

        var candidates = await FindMatchesAsync(solution, query, cancellationToken).ConfigureAwait(false);

        if (candidates.Count == 0)
            return Result.AsError("no member found", new Dictionary<string, string> { ["query"] = query });

        // Prefer handwritten matches when available.
        if (candidates.Any(m => m.IsHandwritten))
            candidates.RemoveAll(m => !m.IsHandwritten);

        // Deduplicate exact duplicates first (same declaration).
        candidates = candidates
            .DistinctBy(m => (m.DocumentId, m.Span, m.Kind))
            .ToList();

        // NOTE: We intentionally do NOT attempt to collapse candidates further here.
        // Even if multiple declarations resolve to a single symbol (e.g. partial methods),
        // doing semantic resolution for every candidate is expensive and risks accidentally
        // collapsing overloads or returning an arbitrary representative.

        // If ambiguous: keep output small.
        if (candidates.Count != 1)
        {
            const int maxMatches = 50;

            var ordered = candidates
                .OrderBy(m => m.FullName, StringComparer.Ordinal)
                .ThenBy(m => m.ProjectPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var truncated = ordered.Count > maxMatches;
            if (truncated)
                ordered = ordered.Take(maxMatches).ToList();

            return new Result(
                ordered
                    .Select(m => new Match(
                        m.FullName,
                        workspaceManager.ToRelativePathIfPossible(m.ProjectPath) ?? m.ProjectPath,
                        workspaceManager.ToRelativePathIfPossible(m.Location) ?? m.Location,
                        m.Kind))
                    .ToList(),
                Member: null,
                Truncated: truncated);
        }

        // If exactly one match: resolve to a stable symbol id and immediately return load_member.
        var match = candidates[0];
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

        var symbol = TryResolveSymbol(node, semanticModel, cancellationToken);
        if (symbol is null)
            return Result.AsError("member symbol not found");

        var symbolId = symbolManager.ToId(symbol);

        var memberResult = await loadMemberTool.Execute(cancellationToken, memberSymbolId: symbolId).ConfigureAwait(false);
        return new Result([], memberResult);
    }

    private sealed record FoundMatch(
        string FullName,
        string Name,
        string ProjectPath,
        DocumentId DocumentId,
        TextSpan Span,
        string? Location,
        string Kind,
        bool IsHandwritten);

    private static ISymbol? TryResolveSymbol(SyntaxNode node, SemanticModel semanticModel, CancellationToken ct)
    {
        // Keep this in sync with what we index.
        // Note: for constructors, we use ConstructorDeclarationSyntax.

        if (node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is { } methodDecl)
            return semanticModel.GetDeclaredSymbol(methodDecl, ct);

        if (node.FirstAncestorOrSelf<ConstructorDeclarationSyntax>() is { } ctorDecl)
            return semanticModel.GetDeclaredSymbol(ctorDecl, ct);

        if (node.FirstAncestorOrSelf<PropertyDeclarationSyntax>() is { } propDecl)
            return semanticModel.GetDeclaredSymbol(propDecl, ct);

        if (node.FirstAncestorOrSelf<EventDeclarationSyntax>() is { } eventDecl)
            return semanticModel.GetDeclaredSymbol(eventDecl, ct);

        if (node.FirstAncestorOrSelf<EventFieldDeclarationSyntax>() is { } eventFieldDecl)
        {
            // "event Action Foo, Bar;" can declare multiple symbols.
            var variable = node.FirstAncestorOrSelf<VariableDeclaratorSyntax>();
            if (variable is not null)
                return semanticModel.GetDeclaredSymbol(variable, ct);
        }

        return null;
    }

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

                foreach (var methodDecl in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                    AddMatch(matches, query, projectPath, document, methodDecl.Identifier.ValueText, methodDecl.Identifier.Span, kind: "method", methodDecl);

                foreach (var ctorDecl in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
                    AddMatch(matches, query, projectPath, document, ctorDecl.Identifier.ValueText, ctorDecl.Identifier.Span, kind: "ctor", ctorDecl);

                foreach (var propDecl in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
                    AddMatch(matches, query, projectPath, document, propDecl.Identifier.ValueText, propDecl.Identifier.Span, kind: "property", propDecl);

                foreach (var evtDecl in root.DescendantNodes().OfType<EventDeclarationSyntax>())
                    AddMatch(matches, query, projectPath, document, evtDecl.Identifier.ValueText, evtDecl.Identifier.Span, kind: "event", evtDecl);

                foreach (var evtField in root.DescendantNodes().OfType<EventFieldDeclarationSyntax>())
                {
                    foreach (var variable in evtField.Declaration.Variables)
                        AddMatch(matches, query, projectPath, document, variable.Identifier.ValueText, variable.Identifier.Span, kind: "event", evtField);
                }
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
        string kind,
        SyntaxNode declarationNode)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
            return;

        var ns = GetNamespace(declarationNode);
        var container = GetContainingTypes(declarationNode);
        var memberIdentity = string.IsNullOrWhiteSpace(container) ? name : $"{container}.{name}";
        var fullName = string.IsNullOrWhiteSpace(ns) ? memberIdentity : $"{ns}.{memberIdentity}";

        var location = document.FilePath;

        matches.Add(new FoundMatch(
            fullName,
            name,
            projectPath,
            document.Id,
            span,
            location,
            kind,
            document.FilePath.IsHandwritten()));
    }

    private static string GetNamespace(SyntaxNode node)
    {
        var segments = new Stack<string>();

        for (SyntaxNode? current = node; current is not null; current = current.Parent)
        {
            if (current is BaseNamespaceDeclarationSyntax ns)
                segments.Push(ns.Name.ToString());
        }

        return segments.Count == 0 ? string.Empty : string.Join(".", segments);
    }

    private static string GetContainingTypes(SyntaxNode node)
    {
        var segments = new Stack<string>();

        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is TypeDeclarationSyntax t)
            {
                var arity = t.TypeParameterList?.Parameters.Count ?? 0;
                var identity = arity == 0 ? t.Identifier.ValueText : $"{t.Identifier.ValueText}`{arity}";
                segments.Push(identity);
            }
        }

        return segments.Count == 0 ? string.Empty : string.Join(".", segments);
    }
}
