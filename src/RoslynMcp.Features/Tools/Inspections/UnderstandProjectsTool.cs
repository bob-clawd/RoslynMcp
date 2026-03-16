using System.ComponentModel;
using ModelContextProtocol.Server;
using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Features.Tools.Inspections;

public sealed class UnderstandProjectsTool(ICodeUnderstandingService codeUnderstandingService) : Tool
{
    private readonly ICodeUnderstandingService _codeUnderstandingService = codeUnderstandingService ?? throw new ArgumentNullException(nameof(codeUnderstandingService));

    [McpServerTool(Name = "understand_projects", Title = "Understand Projects", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need a quick overview of the loaded solution's project landscape. It returns real project relationships with projectPath lists, compact per-project type summaries for standard/deep profiles, and hotspots only for deep analysis.")]
    public Task<UnderstandProjectsResult> ExecuteAsync(CancellationToken cancellationToken,
        [Description("Analysis depth. quick omits types and hotspots, standard includes types, deep includes types and 10 hotspots. Defaults to standard.")]
        string? profile = null
        )
        => _codeUnderstandingService.UnderstandProjectsAsync(profile.ToUnderstandProjectsRequest(), cancellationToken);
}
