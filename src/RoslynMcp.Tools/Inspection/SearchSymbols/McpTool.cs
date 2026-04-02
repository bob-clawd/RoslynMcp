using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.SearchSymbols;

public sealed record Result(
    int TotalCount,
    IReadOnlyList<SymbolMatch> Types,
    IReadOnlyList<SymbolMatch> Members,
    ErrorInfo? Error = null)
{
    public static Result AsError(string message, IReadOnlyDictionary<string, string>? details = null)
        => new(0, [], [], new ErrorInfo(message, details));
}

public sealed record SymbolMatch(
    string SymbolId,
    string Kind,
    string DisplayName,
    string? Location,
    string? ContainerName);

[McpServerToolType]
public sealed class McpTool(
    WorkspaceManager workspaceManager,
    SolutionManager solutionManager,
    SymbolManager symbolManager
    ) : Tool
{
    [McpServerTool(Name = "search_symbols", Title = "Search Symbols", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to find types or members by name pattern across the solution or in a specific project. Supports wildcards (* and ?) and regular expressions.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("Search pattern. Use * for wildcards (e.g., '*Controller', 'I*Service'). Supports ? for single character.")]
        string pattern,
        [Description("Optional project path to limit search scope. If omitted, searches entire solution.")]
        string? projectPath = null,
        [Description("Symbol kinds to include: 'types' (classes, interfaces, enums, etc.), 'members' (methods, properties, fields), or 'all'. Default is 'all'.")]
        string? searchKind = "all"
        )
    {
        if (solutionManager.Solution is not { } solution)
            return Result.AsError("load solution first");

        if (string.IsNullOrWhiteSpace(pattern))
            return Result.AsError("pattern is required");

        // Convert simple wildcard pattern to regex
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        
        Regex? nameMatcher;
        try
        {
            nameMatcher = new Regex(regexPattern, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException ex)
        {
            return Result.AsError("invalid pattern", new Dictionary<string, string> { ["error"] = ex.Message });
        }

        // Determine which projects to search
        var projects = string.IsNullOrEmpty(projectPath) 
            ? solution.Projects.ToList()
            : solution.Projects.Where(p => Matches(p, projectPath)).ToList();

        if (!projects.Any())
            return Result.AsError("no matching project found", new Dictionary<string, string> { ["projectPath"] = projectPath ?? string.Empty });

        var searchTypes = searchKind?.ToLower() is "all" or "types" or null;
        var searchMembers = searchKind?.ToLower() is "all" or "members" or null;

        var typeMatches = new List<SymbolMatch>();
        var memberMatches = new List<SymbolMatch>();

        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) is not { } compilation)
                continue;

            var projectTrees = (await Task.WhenAll(project.Documents
                    .Where(d => d.SupportsSyntaxTree)
                    .Select(d => d.GetSyntaxTreeAsync(cancellationToken))))
                .OfType<SyntaxTree>()
                .ToHashSet();

            if (searchTypes)
            {
                var types = compilation.GlobalNamespace.GetTypes()
                    .Where(type => type.DeclaringSyntaxReferences.Select(r => r.SyntaxTree).Any(projectTrees.Contains))
                    .Where(type => nameMatcher.IsMatch(type.Name));

                foreach (var type in types)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    typeMatches.Add(ToTypeMatch(type));
                }
            }

            if (searchMembers)
            {
                var types = compilation.GlobalNamespace.GetTypes()
                    .Where(type => type.DeclaringSyntaxReferences.Select(r => r.SyntaxTree).Any(projectTrees.Contains));

                foreach (var type in types)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var members = type.GetMembers()
                        .Where(m => m.DeclaredAccessibility > Accessibility.Private)
                        .Where(m => nameMatcher.IsMatch(m.Name));

                    foreach (var member in members)
                    {
                        memberMatches.Add(ToMemberMatch(member, type));
                    }
                }
            }
        }

        return new Result(
            typeMatches.Count + memberMatches.Count,
            typeMatches
                .OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(50)
                .ToList(),
            memberMatches
                .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(100)
                .ToList());
    }

    private bool Matches(Project project, string? input)
        => project.Name == input || project.FilePath == workspaceManager.ToAbsolutePath(input);

    private SymbolMatch ToTypeMatch(INamedTypeSymbol symbol)
    {
        var ns = symbol.ContainingNamespace?.ToDisplayString();
        return new SymbolMatch(
            symbolManager.ToId(symbol),
            symbol.ToTypeKind(),
            symbol.Name,
            symbol.GetLocation(workspaceManager),
            string.IsNullOrEmpty(ns) || ns == "<global namespace>" ? null : ns);
    }

    private SymbolMatch ToMemberMatch(ISymbol symbol, INamedTypeSymbol containingType)
    {
        return new SymbolMatch(
            symbolManager.ToId(symbol),
            symbol.ToMemberKind() ?? "member",
            symbol.Name,
            symbol.GetLocation(workspaceManager),
            containingType.Name);
    }
}
