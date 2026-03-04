using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RoslynMcp.McpServer.Tools;

[McpServerToolType]
public sealed class FindUnusedSymbolsTools
{
    private readonly ICodeUnderstandingService _codeUnderstandingService;

    public FindUnusedSymbolsTools(ICodeUnderstandingService codeUnderstandingService)
    {
        _codeUnderstandingService = codeUnderstandingService ?? throw new ArgumentNullException(nameof(codeUnderstandingService));
    }

    [McpServerTool(Name = "find_unused_symbols", Title = "Find Unused Symbols", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to identify unused or dead code in a project. It finds methods, properties, fields, and types that have few or no references. This is useful for code cleanup and identifying legacy code that can be safely removed. Note: Public API symbols (public/protected) are flagged separately as they may be external entry points.")]
    public Task<FindUnusedSymbolsResult> FindUnusedSymbolsAsync(
        CancellationToken cancellationToken,
        [Description("Exact path to a project file (.csproj). Specify only one of projectPath, projectName, or projectId.")]
        string? projectPath = null,
        [Description("Name of a project. Specify only one of projectPath, projectName, or projectId.")]
        string? projectName = null,
        [Description("Project identifier from load_solution output. Specify only one of projectPath, projectName, or projectId.")]
        string? projectId = null,
        [Description("Filter by symbol kind: method, property, field, event, type, or all (default).")]
        string? kind = null,
        [Description("Filter by accessibility: public, internal, protected, private, or all (default).")]
        string? accessibility = null,
        [Description("Minimum reference count threshold. Symbols with references <= this value are returned. Default is 0 (completely unused).")]
        int? minReferenceCount = null)
        => _codeUnderstandingService.FindUnusedSymbolsAsync(
            projectPath.ToFindUnusedSymbolsRequest(projectName, projectId, kind, accessibility, minReferenceCount),
            cancellationToken);
}
