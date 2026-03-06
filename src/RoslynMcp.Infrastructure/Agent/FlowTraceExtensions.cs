using RoslynMcp.Core;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Infrastructure.Agent;

internal static class FlowTraceExtensions
{
    public static (string Direction, ErrorInfo? Error) NormalizeFlowDirection(this string? direction)
    {
        var normalized = string.IsNullOrWhiteSpace(direction) ? "both" : direction.Trim().ToLowerInvariant();
        return normalized switch
        {
            "upstream" or "up" => ("upstream", null),
            "downstream" or "down" => ("downstream", null),
            "both" => ("both", null),
            _ => ("both", AgentErrorInfo.Create(
                ErrorCodes.InvalidInput,
                "direction must be one of: upstream, downstream, both.",
                "Retry trace_flow with direction set to upstream, downstream, or both.",
                ("field", "direction"),
                ("provided", direction ?? string.Empty),
                ("expected", "upstream|downstream|both")))
        };
    }

    public static string ExtractProjectFromSymbolId(this string symbolId)
    {
        var separator = symbolId.IndexOf(':');
        return separator > 0 ? symbolId[..separator] : "unknown";
    }
}
