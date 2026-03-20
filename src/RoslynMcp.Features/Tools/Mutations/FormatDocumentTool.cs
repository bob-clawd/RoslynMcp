using System.ComponentModel;
using ModelContextProtocol.Server;
using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Workspace;

namespace RoslynMcp.Features.Tools.Mutations;

public sealed class FormatDocumentTool(IRefactoringService refactoringService, ICurrentWorkspaceRootProvider currentWorkspaceRootProvider) : Tool
{
    private readonly IRefactoringService _refactoringService = refactoringService ?? throw new ArgumentNullException(nameof(refactoringService));
    private readonly string _workspaceRoot = currentWorkspaceRootProvider?.WorkspaceRoot ?? throw new ArgumentNullException(nameof(currentWorkspaceRootProvider));

    [McpServerTool(Name = "format_document", Title = "Format Document")]
    [Description("Use this tool when you need to format exactly one C# source document in the loaded solution using the solution's current formatting and style settings. Returns whether formatting changes were applied and persisted.")]
    public Task<FormatDocumentResult> ExecuteAsync(CancellationToken cancellationToken,
        [Description("The path to the C# source file to format. The file must be part of the currently loaded solution.")]
        string path)
    {
        return _refactoringService.FormatDocumentAsync(path.ToFormatDocumentRequest(_workspaceRoot), cancellationToken);
    }
}
