using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.SearchMember;

public sealed record Match(string FullName, string ProjectPath, string? Location, string? Kind, string? Signature = null);

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

        // Prefer exact name matches: if the user searched for "Duration", and there are candidates named exactly
        // "Duration" among broader matches, pick those.
        if (candidates.Any(m => string.Equals(m.Name, query, StringComparison.OrdinalIgnoreCase)))
            candidates.RemoveAll(m => !string.Equals(m.Name, query, StringComparison.OrdinalIgnoreCase));

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
                        m.Kind,
                        m.Signature))
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
        string? Signature,
        bool IsHandwritten);

    private static ISymbol? TryResolveSymbol(SyntaxNode node, SemanticModel semanticModel, CancellationToken ct)
    {
        // Keep this in sync with what we index.
        // Note: for constructors, we use ConstructorDeclarationSyntax.

        if (node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is { } methodDecl)
            return semanticModel.GetDeclaredSymbol(methodDecl, ct);

        if (node.FirstAncestorOrSelf<ConstructorDeclarationSyntax>() is { } ctorDecl)
            return semanticModel.GetDeclaredSymbol(ctorDecl, ct);

        // Primary constructors (C# 12) are declared on the type header.
        // For unique matches we need to resolve the constructor symbol from the containing type.
        if (node.FirstAncestorOrSelf<TypeDeclarationSyntax>() is { ParameterList: not null } typeDecl)
        {
            if (semanticModel.GetDeclaredSymbol(typeDecl, ct) is INamedTypeSymbol typeSymbol)
            {
                // Prefer the instance ctor that is declared in source (ignore static ctor).
                // For primary ctors, this should map back to the type declaration.
                var ctor = typeSymbol.InstanceConstructors
                    .Where(c => c.DeclaringSyntaxReferences.Length > 0)
                    .FirstOrDefault();

                if (ctor is not null)
                    return ctor;
            }
        }

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

                // Primary constructors (C# 12): e.g. `class Foo(Dep dep) { }`
                // Roslyn represents them on the type declaration header.
                foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (typeDecl.ParameterList is null)
                        continue;

                    // For primary ctors, the only meaningful identifier the user can search for is the type name.
                    // We don't want broad queries to accidentally match type names.
                    if (!string.Equals(typeDecl.Identifier.ValueText, query, StringComparison.OrdinalIgnoreCase))
                        continue;

                    AddMatch(matches, query, projectPath, document, typeDecl.Identifier.ValueText, typeDecl.Identifier.Span, kind: "ctor", typeDecl);
                }

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

        var signature = kind switch
        {
            "method" => (declarationNode as MethodDeclarationSyntax)?.ParameterList?.ToString(),
            "ctor" => (declarationNode as ConstructorDeclarationSyntax)?.ParameterList?.ToString(),
            _ => null
        };

        matches.Add(new FoundMatch(
            fullName,
            name,
            projectPath,
            document.Id,
            span,
            location,
            kind,
            signature,
            document.FilePath.IsHandwritten()));
    }

	private static string GetNamespace(SyntaxNode node) => node.GetNamespaceName();

	private static string GetContainingTypes(SyntaxNode node) => node.GetContainingTypeChain();
}
