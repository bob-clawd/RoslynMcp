using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RoslynMcp.McpServer.Tools;

[McpServerToolType]
public sealed class UnderstandCodebaseTools
{
    private readonly ICodeUnderstandingService _codeUnderstandingService;

    public UnderstandCodebaseTools(ICodeUnderstandingService codeUnderstandingService)
    {
        _codeUnderstandingService = codeUnderstandingService ?? throw new ArgumentNullException(nameof(codeUnderstandingService));
    }

    [McpServerTool(Name = "understand_codebase", Title = "Understand Codebase", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need a quick overview of the codebase structure at the start of a session. It returns the project structure with dependency relationships and identifies hotspots — the most complex and heavily-commented methods that are likely worth attention.")]
    public Task<UnderstandCodebaseResult> UnderstandCodebaseAsync(
        CancellationToken cancellationToken,
        [Description("Analysis depth. quick for fast results, standard for balanced output, deep for thorough analysis. Defaults to standard.")]
        string? profile = null)
        => _codeUnderstandingService.UnderstandCodebaseAsync(ToolContractMapper.ToUnderstandCodebaseRequest(profile), cancellationToken);
}
