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
    bool? Truncated = null,
    ErrorInfo? Error = null)
{
    public static Result AsError(string message, IReadOnlyDictionary<string, string>? details = null)
        => new([], null, null, new ErrorInfo(message, details));
}

[McpServerToolType]
public sealed class McpTool(WorkspaceManager workspaceManager, SolutionManager solutionManager, SymbolManager symbolManager, Inspection.LoadMember.McpTool loadMemberTool) : Tool
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

        var candidates = await FindCandidatesAsync(solution, query, cancellationToken).ConfigureAwait(false);

        if (candidates.Count == 0)
            return NoMemberFound(query);

        candidates = SelectCandidates(candidates, query);

        return candidates.Count == 1
            ? await LoadUniqueMember(solution, candidates[0], cancellationToken).ConfigureAwait(false)
            : BuildAmbiguousResult(candidates);
    }

    private static Result NoMemberFound(string query)
        => Result.AsError("no member found", new Dictionary<string, string> { ["query"] = query });

    private static List<FoundMatch> SelectCandidates(List<FoundMatch> candidates, string query)
        => PreferExactNameMatches(CollapseRepeatedDeclarations(PreferHandwritten(candidates)), query);

    private static List<FoundMatch> PreferHandwritten(List<FoundMatch> candidates)
    {
        if (!candidates.Any(m => m.IsHandwritten))
            return candidates;

        return candidates.Where(m => m.IsHandwritten).ToList();
    }

    private static List<FoundMatch> CollapseRepeatedDeclarations(List<FoundMatch> candidates)
        => candidates
            .DistinctBy(m => (m.FullName, m.ProjectPath, m.Kind, m.Signature))
            .ToList();

    private static List<FoundMatch> PreferExactNameMatches(List<FoundMatch> candidates, string query)
    {
        var exactNameMatches = candidates
            .Where(m => string.Equals(m.Name, query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return exactNameMatches.Count == 0 ? candidates : exactNameMatches;
    }

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
                workspaceManager.ToRelativePathIfPossible(m.ProjectPath) ?? m.ProjectPath,
                workspaceManager.ToRelativePathIfPossible(m.Location) ?? m.Location,
                m.Kind,
                m.Signature))
            .ToList();

        return truncated
            ? new Result(matches, Truncated: true)
            : new Result(matches);
    }

    private async Task<Result> LoadUniqueMember(Solution solution, FoundMatch match, CancellationToken cancellationToken)
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
        if (node.FirstAncestorOrSelf<MethodDeclarationSyntax>() is { } methodDecl)
            return semanticModel.GetDeclaredSymbol(methodDecl, ct);

        if (node.FirstAncestorOrSelf<ConstructorDeclarationSyntax>() is { } ctorDecl)
            return semanticModel.GetDeclaredSymbol(ctorDecl, ct);

        if (node.FirstAncestorOrSelf<TypeDeclarationSyntax>() is { ParameterList: not null } typeDecl)
        {
            if (semanticModel.GetDeclaredSymbol(typeDecl, ct) is INamedTypeSymbol typeSymbol)
            {
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

                AddMatches(matches, query, projectPath, document, root);
            }
        }

        return matches;
    }

    private static void AddMatches(List<FoundMatch> matches, string query, string projectPath, Document document, SyntaxNode root)
    {
        foreach (var methodDecl in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            AddMatch(matches, query, projectPath, document, methodDecl.Identifier.ValueText, methodDecl.Identifier.Span, kind: "method", methodDecl);

        foreach (var ctorDecl in root.DescendantNodes().OfType<ConstructorDeclarationSyntax>())
            AddMatch(matches, query, projectPath, document, ctorDecl.Identifier.ValueText, ctorDecl.Identifier.Span, kind: "ctor", ctorDecl);

        AddPrimaryConstructorMatches(matches, query, projectPath, document, root);

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

    private static void AddPrimaryConstructorMatches(List<FoundMatch> matches, string query, string projectPath, Document document, SyntaxNode root)
    {
        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (typeDecl.ParameterList is null)
                continue;

            if (!string.Equals(typeDecl.Identifier.ValueText, query, StringComparison.OrdinalIgnoreCase))
                continue;

            AddMatch(matches, query, projectPath, document, typeDecl.Identifier.ValueText, typeDecl.Identifier.Span, kind: "ctor", typeDecl);
        }
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

        matches.Add(new FoundMatch(
            BuildFullName(declarationNode, name),
            name,
            projectPath,
            document.Id,
            span,
            document.FilePath,
            kind,
            GetSignature(kind, declarationNode),
            document.FilePath.IsHandwritten()));
    }

    private static string BuildFullName(SyntaxNode declarationNode, string name)
    {
        var ns = declarationNode.GetNamespaceName();
        var container = declarationNode.GetContainingTypeChain();
        var explicitInterfacePrefix = GetExplicitInterfacePrefix(declarationNode);
        var memberName = string.IsNullOrWhiteSpace(explicitInterfacePrefix) ? name : $"{explicitInterfacePrefix}.{name}";
        var memberIdentity = string.IsNullOrWhiteSpace(container) ? memberName : $"{container}.{memberName}";
        return string.IsNullOrWhiteSpace(ns) ? memberIdentity : $"{ns}.{memberIdentity}";
    }

    private static string? GetExplicitInterfacePrefix(SyntaxNode declarationNode) => declarationNode switch
    {
        MethodDeclarationSyntax { ExplicitInterfaceSpecifier: not null } method => method.ExplicitInterfaceSpecifier.Name.ToString(),
        PropertyDeclarationSyntax { ExplicitInterfaceSpecifier: not null } property => property.ExplicitInterfaceSpecifier.Name.ToString(),
        EventDeclarationSyntax { ExplicitInterfaceSpecifier: not null } @event => @event.ExplicitInterfaceSpecifier.Name.ToString(),
        _ => null
    };

    private static string? GetSignature(string kind, SyntaxNode declarationNode) => kind switch
    {
        "method" => declarationNode is MethodDeclarationSyntax method
            ? $"{method.TypeParameterList}{method.ParameterList}"
            : null,
        "ctor" => declarationNode switch
        {
            ConstructorDeclarationSyntax constructor => constructor.ParameterList.ToString(),
            TypeDeclarationSyntax { ParameterList: not null } type => type.ParameterList!.ToString(),
            _ => null
        },
        _ => null
    };

}
