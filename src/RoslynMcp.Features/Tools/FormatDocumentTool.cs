using ModelContextProtocol.Server;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using System.ComponentModel;

namespace RoslynMcp.Features.Tools;

public sealed class FormatDocumentTool(IRefactoringService refactoringService) : Tool
{
    private readonly IRefactoringService _refactoringService = refactoringService ?? throw new ArgumentNullException(nameof(refactoringService));

    [McpServerTool(Name = "format_document", Title = "Format Document", ReadOnly = false, Idempotent = false)]
    [Description("Formats a C# source document according to the solution's code style settings. Use this when you need to apply consistent formatting (indentation, spacing, line breaks) to a specific file.")]
    public Task<FormatDocumentResult> ExecuteAsync(CancellationToken cancellationToken,
        [Description("Path to the C# source file to format. The file must exist in the currently loaded solution.")]
        string path)
        => _refactoringService.FormatDocumentAsync(new FormatDocumentRequest(path), cancellationToken);
}
