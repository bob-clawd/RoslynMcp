using ModelContextProtocol.Server;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Core;
using System.ComponentModel;

namespace RoslynMcp.Features.Tools;

public sealed class ExplainSymbolTool(ICodeUnderstandingService codeUnderstandingService) : Tool
{
    private readonly ICodeUnderstandingService _codeUnderstandingService = codeUnderstandingService ?? throw new ArgumentNullException(nameof(codeUnderstandingService));

    [McpServerTool(Name = "explain_symbol", Title = "Explain Symbol", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to understand what a specific symbol (type, method, property, field, etc.) does, what its signature looks like, and where it is used in the codebase. It provides a human-readable explanation along with impact hints showing areas with high reference density.")]
    public Task<ExplainSymbolResult> ExecuteAsync(CancellationToken cancellationToken,
        [Description("The stable symbol ID, obtained from resolve_symbol, list_types, or list_members. Provide this OR path+line+column.")]
        string? symbolId = null,
        [Description("Path to a source file. Provide this together with line and column instead of symbolId.")]
        string? path = null,
        [Description("Line number (1-based) pointing to the symbol in the source file.")]
        int? line = null,
        [Description("Column number (1-based) pointing to the symbol in the source file.")]
        int? column = null
        )
        => _codeUnderstandingService.ExplainSymbolAsync(symbolId.ToExplainSymbolRequest(path, line, column), cancellationToken);
}
