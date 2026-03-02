using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RoslynMcp.McpServer.Tools;

[McpServerToolType]
public sealed class ListDependenciesTools
{
    private readonly ICodeUnderstandingService _codeUnderstandingService;

    public ListDependenciesTools(ICodeUnderstandingService codeUnderstandingService)
    {
        _codeUnderstandingService = codeUnderstandingService ?? throw new ArgumentNullException(nameof(codeUnderstandingService));
    }

    [McpServerTool(Name = "list_dependencies", Title = "List Dependencies", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to understand how projects relate to each other within a solution. It shows the dependency graph between projects, indicating which projects depend on which others.")]
    public Task<ListDependenciesResult> ListDependenciesAsync(
        CancellationToken cancellationToken,
        [Description("Exact path to a project file (.csproj). Specify only one of projectPath, projectName, or projectId.")]
        string? projectPath = null,
        [Description("Name of a project. Specify only one of projectPath, projectName, or projectId.")]
        string? projectName = null,
        [Description("Project identifier from load_solution output. Specify only one of projectPath, projectName, or projectId.")]
        string? projectId = null,
        [Description("Which direction of dependencies to return. outgoing shows what the selected project depends on. incoming shows what depends on the selected project. both returns both directions. Defaults to both.")]
        string? direction = null)
        => _codeUnderstandingService.ListDependenciesAsync(
            ToolContractMapper.ToListDependenciesRequest(projectPath, projectName, projectId, direction),
            cancellationToken);
}
