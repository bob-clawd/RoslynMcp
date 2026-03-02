using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RoslynMcp.McpServer.Tools;

[McpServerToolType]
public sealed class ListMembersTools
{
    private readonly ICodeUnderstandingService _codeUnderstandingService;

    public ListMembersTools(ICodeUnderstandingService codeUnderstandingService)
    {
        _codeUnderstandingService = codeUnderstandingService ?? throw new ArgumentNullException(nameof(codeUnderstandingService));
    }

    [McpServerTool(Name = "list_members", Title = "List Members", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to see what members (methods, properties, fields, events, constructors) exist inside a specific type. This helps you understand the structure and capabilities of a class or interface.")]
    public Task<ListMembersResult> ListMembersAsync(
        CancellationToken cancellationToken,
        [Description("The stable symbol ID of a type, obtained from list_types. Provide this OR path+line+column.")]
        string? typeSymbolId = null,
        [Description("Path to a source file. Provide this together with line and column instead of typeSymbolId.")]
        string? path = null,
        [Description("Line number (1-based) pointing to a type in the source file.")]
        int? line = null,
        [Description("Column number (1-based) pointing to a type in the source file.")]
        int? column = null,
        [Description("Filter by member kind: method, property, field, event, or ctor.")]
        string? kind = null,
        [Description("Filter by accessibility: public, internal, protected, private, protected_internal, or private_protected.")]
        string? accessibility = null,
        [Description("Filter by binding type: static or instance.")]
        string? binding = null,
        [Description("When true, includes members from base classes. Defaults to false.")]
        bool? includeInherited = null,
        [Description("Maximum number of results to return. Defaults to 100, maximum 500.")]
        int? limit = null,
        [Description("Number of results to skip for pagination. Defaults to 0.")]
        int? offset = null)
        => _codeUnderstandingService.ListMembersAsync(
            ToolContractMapper.ToListMembersRequest(typeSymbolId, path, line, column, kind, accessibility, binding, includeInherited, limit, offset),
            cancellationToken);
}
