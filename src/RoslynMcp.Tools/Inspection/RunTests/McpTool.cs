using System.ComponentModel;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.RunTests;

[McpServerToolType]
public sealed class McpTool(WorkspaceManager workspaceManager, SolutionManager solutionManager) : Tool
{
    [McpServerTool(Name = "run_tests", Title = "Run Tests", ReadOnly = false, Idempotent = false)]
    [Description("Default .NET test runner. Use this instead of 'dotnet test' unless you need unsupported CLI behavior.")]
    public async Task<Result> Execute(
        CancellationToken cancellationToken,
        [Description("Optional execution target. Omit to run the currently loaded solution, or the workspace directory when no solution is loaded. Supports workspace-relative or absolute .sln, .slnx, .csproj, or directory paths when the resolved target stays within the workspace directory.")]
        string? target = null,
        [Description("Optional dotnet test filter expression. Passed through to --filter semantics where practical.")]
        string? filter = null)
    {
        target ??= solutionManager.Solution?.FilePath ?? workspaceManager.WorkspaceDirectory;
        target = workspaceManager.ToAbsolutePath(target);
        
        if(!target.IsWithin(workspaceManager.WorkspaceDirectory))
            return Result.AsError("target must be in the workspace directory");
        
        if (!Directory.Exists(target) && !File.Exists(target))
            return Result.AsError("target not found");

        if (File.Exists(target)
            && !target.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            && !target.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            && !target.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return Result.AsError("target must be a directory, .sln, .slnx, or .csproj");
        }
        
        try
        {
            return await DotNet.Test(workspaceManager, target, filter, cancellationToken);
        }
        catch (Exception e)
        {
            return Result.AsError(e.Message, new Dictionary<string, string>
            {
                ["inner exception"] = e.InnerException?.Message ?? string.Empty,
                ["stack trace"] = e.StackTrace ?? string.Empty
            });
        }
    }
}
