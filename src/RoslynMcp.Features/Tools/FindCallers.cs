using ModelContextProtocol.Server;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Core;
using System.ComponentModel;

namespace RoslynMcp.Features.Tools;

public sealed class FindCallersTool(IFlowTraceService flowTraceService) : Tool
{
    private readonly IFlowTraceService _flowTraceService = flowTraceService ?? throw new ArgumentNullException(nameof(flowTraceService));

    [McpServerTool(Name = "find_callers", Title = "Find Callers", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need only the immediate direct upstream callers of a symbol. This is a focused wrapper around call-flow tracing and does not traverse beyond one caller level.")]
    public Task<TraceFlowResult> ExecuteAsync(CancellationToken cancellationToken,
        [Description("The stable symbol ID, obtained from resolve_symbol, list_types, or list_members, for the symbol whose immediate direct callers you want to inspect.")]
        string? symbolId = null
        )
        => _flowTraceService.TraceFlowAsync(symbolId.ToTraceFlowRequest(null, null, null, "upstream", 1), cancellationToken);
}
