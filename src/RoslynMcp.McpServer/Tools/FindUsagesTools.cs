using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Navigation;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RoslynMcp.McpServer.Tools;

[McpServerToolType]
public sealed class FindUsagesTools
{
    private readonly INavigationService _navigationService;

    public FindUsagesTools(INavigationService navigationService)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    }

    [McpServerTool(Name = "find_usages", Title = "Find Usages", ReadOnly = true, Idempotent = true)]
    [Description("Finds references/usages of a symbol within a specific scope: 'document', 'project', or 'solution'. Use when you want to limit search to a specific file or project. Requires symbolId and scope.")]
    public Task<FindReferencesScopedResult> FindUsagesAsync(
        CancellationToken cancellationToken,
        [Description("Canonical symbolId from resolve_symbol, list_types, or list_members.")]
        string symbolId,
        [Description("Search scope: 'document' (current file only), 'project' (containing project), or 'solution' (all projects, default).")]
        string scope = "solution",
        [Description("Required when scope='document': the file path to search within.")]
        string? path = null)
        => _navigationService.FindReferencesScopedAsync(
            ToolContractMapper.ToFindReferencesScopedRequest(symbolId, scope, path),
            cancellationToken);
}