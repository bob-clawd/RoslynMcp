using ModelContextProtocol.Server;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Core;
using System.ComponentModel;

namespace RoslynMcp.Features.Tools;

public sealed class FindCodeSmellsTool(ICodeSmellFindingService codeSmellFindingService) : Tool
{
    private readonly ICodeSmellFindingService _codeSmellFindingService = codeSmellFindingService ?? throw new ArgumentNullException(nameof(codeSmellFindingService));

    [McpServerTool(Name = "find_codesmells", Title = "Find Code Smells", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to check a specific file for potential code quality issues. It runs Roslyn-based static analysis to detect common problems such as dead code, performance anti-patterns, naming violations, and other code smells identified by Roslynator analyzers.")]
    public Task<FindCodeSmellsResult> ExecuteAsync(CancellationToken cancellationToken,
        [Description("Path to the source file to analyze. The file must exist in the currently loaded solution.")]
        string path
        )
        => _codeSmellFindingService.FindCodeSmellsAsync(path.ToFindCodeSmellsRequest(), cancellationToken);
}
