using ModelContextProtocol.Server;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Core;
using System.ComponentModel;

namespace RoslynMcp.Features.Tools;

public sealed class FindImplementationsTool(INavigationService navigationService) : Tool
{
    private readonly INavigationService _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));

    [McpServerTool(Name = "find_implementations", Title = "Find Implementations", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to find all implementations of an interface, abstract class, or abstract/virtual method. This is essential for understanding polymorphism — where interfaces are implemented or where abstract members are overridden.")]
    public Task<FindImplementationsResult> ExecuteAsync(CancellationToken cancellationToken,
        [Description("The stable symbol ID of an interface, abstract class, or abstract/virtual method, obtained from resolve_symbol, list_types, or list_members.")]
        string symbolId
        )
        => _navigationService.FindImplementationsAsync(symbolId.ToFindImplementationsRequest(), cancellationToken);
}
