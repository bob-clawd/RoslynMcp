using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RoslynMcp.McpServer.Tools;

[McpServerToolType]
public sealed class LoadSolutionTools
{
    private readonly IWorkspaceBootstrapService _workspaceBootstrapService;

    public LoadSolutionTools(IWorkspaceBootstrapService workspaceBootstrapService)
    {
        _workspaceBootstrapService = workspaceBootstrapService ?? throw new ArgumentNullException(nameof(workspaceBootstrapService));
    }

    [McpServerTool(Name = "load_solution", Title = "Load Solution", ReadOnly = false, Idempotent = false)]
    [Description("Use this tool when you need to start working with a .NET solution and no solution has been loaded yet. This must be the first tool called in a session before any code analysis or navigation tools can be used.")]
    public Task<LoadSolutionResult> LoadSolutionAsync(
        CancellationToken cancellationToken,
        [Description("(optional): Absolute path to the `.sln` file. If not provided, the tool will attempt to auto-detect a solution file.")]
        string? solutionHintPath = null)
        => _workspaceBootstrapService.LoadSolutionAsync(ToolContractMapper.ToLoadSolutionRequest(solutionHintPath), cancellationToken);
}