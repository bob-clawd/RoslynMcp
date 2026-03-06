using ModelContextProtocol.Server;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Core;
using System.ComponentModel;

namespace RoslynMcp.Features.Tools;

public sealed class ResolveSymbolTool(ICodeUnderstandingService codeUnderstandingService) : Tool
{
    private readonly ICodeUnderstandingService _codeUnderstandingService = codeUnderstandingService ?? throw new ArgumentNullException(nameof(codeUnderstandingService));

    [McpServerTool(Name = "resolve_symbol", Title = "Resolve Symbol", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you have a location in source code (file path + line + column) or a qualified name and you need to obtain a stable symbolId that can be used by other navigation tools. This is often the first step before calling explain_symbol, trace_call_flow, or find_usages.")]
    public Task<ResolveSymbolResult> ExecuteAsync(CancellationToken cancellationToken,
        [Description("An existing symbol ID to look up. Provide this OR path+line+column OR qualifiedName.")]
        string? symbolId = null,
        [Description("Path to a source file. Provide this together with line and column instead of symbolId or qualifiedName.")]
        string? path = null,
        [Description("Line number (1-based) in the source file.")]
        int? line = null,
        [Description("Column number (1-based) in the source file.")]
        int? column = null,
        [Description("A fully qualified or short type/member name (e.g., System.String or MyClass.MyMethod). Provide this instead of symbolId or path+line+column.")]
        string? qualifiedName = null,
        [Description("Required when using qualifiedName — path to a project that contains the symbol.")]
        string? projectPath = null,
        [Description("Required when using qualifiedName — name of a project that contains the symbol.")]
        string? projectName = null,
        [Description("Required when using qualifiedName — project ID from load_solution that contains the symbol.")]
        string? projectId = null
        )
        => _codeUnderstandingService.ResolveSymbolAsync(symbolId.ToResolveSymbolRequest(path, line, column, qualifiedName, projectPath, projectName, projectId), cancellationToken);
}
