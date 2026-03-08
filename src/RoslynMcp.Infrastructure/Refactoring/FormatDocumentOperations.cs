using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;

namespace RoslynMcp.Infrastructure.Refactoring;

internal static class FormatDocumentOperations
{
    public static async Task<FormatDocumentResult> FormatDocumentAsync(
        RefactoringOperationOrchestrator orchestrator,
        FormatDocumentRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Path))
        {
            return new FormatDocumentResult(
                string.Empty,
                false,
                RefactoringOperationExtensions.CreateError(ErrorCodes.InvalidInput,
                    "Path is required and cannot be empty.",
                    ("operation", "format_document")));
        }

        var (solution, error) = await orchestrator.TryGetSolutionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new FormatDocumentResult(
                string.Empty,
                false,
                error ?? new ErrorInfo(ErrorCodes.InternalError, "Unable to access the current solution."));
        }

        var document = solution.Projects
            .SelectMany(static project => project.Documents)
            .FirstOrDefault(d => d.FilePath.MatchesByNormalizedPath(request.Path));
        
        if (document == null)
        {
            return new FormatDocumentResult(
                request.Path,
                false,
                RefactoringOperationExtensions.CreateError(ErrorCodes.PathOutOfScope,
                    "The provided path is outside the selected solution scope.",
                    ("operation", "format_document"),
                    ("path", request.Path)));
        }

        try
        {
            var formatted = await Formatter.FormatAsync(document, cancellationToken: ct).ConfigureAwait(false);
            var changed = formatted != document;
            
            return new FormatDocumentResult(
                request.Path,
                changed,
                null);
        }
        catch (Exception)
        {
            return new FormatDocumentResult(
                request.Path,
                false,
                new ErrorInfo(ErrorCodes.InternalError, "Failed to format document due to an unexpected error."));
        }
    }
}
