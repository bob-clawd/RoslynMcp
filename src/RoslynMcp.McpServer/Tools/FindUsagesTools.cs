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
    [Description("Use this tool when you need to find all places where a specific symbol is referenced across a project or the entire solution. This is critical before refactoring or modifying any symbol to understand its impact.")]
    public Task<FindReferencesScopedResult> FindUsagesAsync(
        CancellationToken cancellationToken,
        [Description("The stable symbol ID, obtained from resolve_symbol, list_types, or list_members.")]
        string symbolId,
        [Description("The search scope. project searches only within the containing project. solution searches the entire solution. Defaults to solution.")]
        string scope = "solution",
        [Description("Required when scope=document: the file path to search within.")]
        string? path = null)
        => _navigationService.FindReferencesScopedAsync(
            ToolContractMapper.ToFindReferencesScopedRequest(symbolId, scope, path),
            cancellationToken);
}