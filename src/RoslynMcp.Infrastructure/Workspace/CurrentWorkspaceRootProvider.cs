using RoslynMcp.Core.Models;

namespace RoslynMcp.Infrastructure.Workspace;

public sealed class CurrentWorkspaceRootProvider : ICurrentWorkspaceRootProvider
{
    internal CurrentWorkspaceRootProvider(IWorkspaceRootDiscovery workspaceRootDiscovery)
    {
        ArgumentNullException.ThrowIfNull(workspaceRootDiscovery);

        var (normalizedRoot, error) = workspaceRootDiscovery.NormalizeWorkspaceRoot(Directory.GetCurrentDirectory());
        if (error is not null || string.IsNullOrWhiteSpace(normalizedRoot))
        {
            throw new InvalidOperationException(error?.Message ?? "Failed to determine the current workspace root.");
        }

        WorkspaceRoot = normalizedRoot;
    }

    public string WorkspaceRoot { get; }
}
