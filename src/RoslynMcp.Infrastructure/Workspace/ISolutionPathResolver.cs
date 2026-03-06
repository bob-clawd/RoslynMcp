using RoslynMcp.Core.Models;

namespace RoslynMcp.Infrastructure.Workspace;

internal interface ISolutionPathResolver
{
    (string? ResolvedPath, string? WorkspaceRoot, ErrorInfo? Error) ResolveSolutionPath(string? requestedPath, string? workspaceRootHint);
}
