using ModelContextProtocol.Server;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Core;
using System.ComponentModel;

namespace RoslynMcp.Features.Tools;

public sealed class GetTypeHierarchyTool(INavigationService navigationService) : Tool
{
    private readonly INavigationService _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));

    [McpServerTool(Name = "get_type_hierarchy", Title = "Get Type Hierarchy", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to understand the inheritance relationships of a type — its base classes, implemented interfaces, and any derived types. This helps you understand type evolution and polymorphism in your codebase.")]
    public Task<GetTypeHierarchyResult> ExecuteAsync(CancellationToken cancellationToken,
        [Description("The stable symbol ID of a type, obtained from resolve_symbol, list_types, or list_members. Must resolve to a type (class, interface, enum, struct, or record).")]
        string symbolId,
        [Description("When true (default), includes all transitive base types and all derived types. When false, returns only immediate parents and children.")]
        bool includeTransitive = true,
        [Description("Maximum number of derived types to return. Defaults to 200. Higher values may impact performance.")]
        int maxDerived = 200
        )
        => _navigationService.GetTypeHierarchyAsync(symbolId.ToGetTypeHierarchyRequest(includeTransitive, maxDerived), cancellationToken);
}
