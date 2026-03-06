using ModelContextProtocol.Server;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Core;
using System.ComponentModel;

namespace RoslynMcp.Features.Tools;

public sealed class ListTypesTool(ICodeUnderstandingService codeUnderstandingService) : Tool
{
    private readonly ICodeUnderstandingService _codeUnderstandingService = codeUnderstandingService ?? throw new ArgumentNullException(nameof(codeUnderstandingService));

    [McpServerTool(Name = "list_types", Title = "List Types", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to discover all types (classes, interfaces, enums, structs, records) defined in a specific project. This is useful when you want to explore what's available in a project or find a specific type by name.")]
    public Task<ListTypesResult> ExecuteAsync(CancellationToken cancellationToken,
        [Description("Exact path to a project file (.csproj). Specify only one of projectPath, projectName, or projectId.")]
        string? projectPath = null,
        [Description("Name of a project. Specify only one of projectPath, projectName, or projectId.")]
        string? projectName = null,
        [Description("Project identifier from load_solution output. Specify only one of projectPath, projectName, or projectId.")]
        string? projectId = null,
        [Description("Filter to only types in namespaces starting with this prefix.")]
        string? namespacePrefix = null,
        [Description("Filter by type kind: class, record, interface, enum, or struct.")]
        string? kind = null,
        [Description("Filter by accessibility: public, internal, protected, private, protected_internal, or private_protected.")]
        string? accessibility = null,
        [Description("Maximum number of results to return. Defaults to 100, maximum 500.")]
        int? limit = null,
        [Description("Number of results to skip for pagination. Defaults to 0.")]
        int? offset = null
        )
        => _codeUnderstandingService.ListTypesAsync(projectPath.ToListTypesRequest(projectName, projectId, namespacePrefix, kind, accessibility, limit, offset), cancellationToken);
}
