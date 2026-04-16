using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.LoadProject;

public sealed record Result(
    int TypeCount,
    IReadOnlyList<Entry> Types,
    ErrorInfo? Error = null)
{
    public static Result AsError(string message, IReadOnlyDictionary<string, string>? details = null)
        => new(0, [], new ErrorInfo(message, details));
}

public sealed record Entry(
    TypeSymbol? Type,
    int Members);

[McpServerToolType]
public sealed class McpTool(WorkspaceManager workspaceManager, SolutionManager solutionManager, SymbolManager symbolManager) : Tool
{
    [McpServerTool(Name = "load_project", Title = "Load Project", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to list types declared in a specific project. It is useful for project-scoped discovery, for finding type symbols before follow-up calls such as load_type or load_member.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("Project path from load_solution (relative or absolute .csproj path), or a loaded project name.")]
        string? projectPath = null
        )
    {
        if (solutionManager.Solution is not { } solution)
            return Result.AsError("load solution first");

        if (solution.Projects.FirstOrDefault(p => Matches(p, projectPath)) is not { } project)
            return Result.AsError("no project found", new Dictionary<string, string> { ["projectPath"] = projectPath ?? string.Empty });

        if (await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) is not { } compilation)
            return Result.AsError("no compilation found", new Dictionary<string, string> { ["projectPath"] = projectPath ?? project.FilePath ?? project.Name });

        var projectTrees = (await Task.WhenAll(project.Documents
                .Where(d => d.SupportsSyntaxTree)
                .Select(d => d.GetSyntaxTreeAsync(cancellationToken))))
            .OfType<SyntaxTree>()
            .ToHashSet();
        
        var types = compilation!.GlobalNamespace.GetTypes()
            .Where(type => type.DeclaringSyntaxReferences.Select(r => r.SyntaxTree).Any(projectTrees.Contains))
            .Select(ToEntry)
            .ToList();

        if (types.Any(entry => entry.Type?.Location.IsHandwritten() ?? false))
            types.RemoveAll(entry => entry.Type?.Location.IsHandwritten() != true);

        return new Result(types.Count, types.OrderByDescending(e => e.Members).ToList());
    }

    private bool Matches(Project project, string? input)
        => project.Name == input || project.FilePath == workspaceManager.ToAbsolutePath(input);

    private Entry ToEntry(INamedTypeSymbol symbol)
    {
        return new Entry(TypeSymbol.From(symbol, symbolManager, workspaceManager), symbol.MembersCount());
    }
}
