using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Infrastructure.Refactoring;

internal sealed class DeleteMethodOperations
{
    private readonly RefactoringOperationOrchestrator _owner;
    private readonly MethodMutationTargetResolver _targetResolver;
    private readonly DocumentDiagnosticsDeltaService _diagnosticsDeltaService;

    public DeleteMethodOperations(RefactoringOperationOrchestrator owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _targetResolver = new MethodMutationTargetResolver(owner);
        _diagnosticsDeltaService = new DocumentDiagnosticsDeltaService();
    }

    public async Task<DeleteMethodResult> DeleteMethodAsync(DeleteMethodRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var targetMethodSymbolId = request.TargetMethodSymbolId.NormalizeAcceptedSymbolIdForOutput();

        var validationError = request.ValidateDeleteMethod();
        if (validationError != null)
        {
            return validationError;
        }

        try
        {
            var (solution, version, error) = await _owner.TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return RefactoringOperationExtensions.CreateDeleteMethodErrorResult(targetMethodSymbolId, error);
            }

            var (target, resolveError) = await _targetResolver.ResolveMethodAsync(request.TargetMethodSymbolId, solution, "delete_method", ct).ConfigureAwait(false);
            if (target == null)
            {
                return RefactoringOperationExtensions.CreateDeleteMethodErrorResult(targetMethodSymbolId, resolveError);
            }

            var root = await target.Document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (root == null)
            {
                return RefactoringOperationExtensions.CreateDeleteMethodErrorResult(
                    targetMethodSymbolId,
                    ErrorCodes.InternalError,
                    "Failed to load the target document syntax tree.",
                    ("targetMethodSymbolId", targetMethodSymbolId),
                    ("operation", "delete_method"));
            }

            var deletedMethod = new DeletedMethodInfo(
                targetMethodSymbolId,
                target.MethodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

            var updatedRoot = root.RemoveNode(target.Declaration, SyntaxRemoveOptions.KeepNoTrivia);
            if (updatedRoot == null)
            {
                return RefactoringOperationExtensions.CreateDeleteMethodErrorResult(
                    targetMethodSymbolId,
                    ErrorCodes.TargetNotSourceEditable,
                    "The target method could not be removed deterministically from the document.",
                    ("targetMethodSymbolId", targetMethodSymbolId),
                    ("operation", "delete_method"));
            }

            var updatedDocument = target.Document.WithSyntaxRoot(updatedRoot);
            var updatedSolution = await _owner.FormatScopeAsync(updatedDocument.Project.Solution, [updatedDocument], ct).ConfigureAwait(false);

            var changedFiles = (await solution.CollectChangedFilesAsync(updatedSolution, ct).ConfigureAwait(false))
                .Select(static file => file.FilePath)
                .ToArray();

            var diagnosticsDelta = await _diagnosticsDeltaService
                .GetDeltaAsync(solution, updatedSolution, target.Document.Id, ct)
                .ConfigureAwait(false);

            var stillResolved = await _owner.ResolveSymbolAsync(request.TargetMethodSymbolId, updatedSolution, ct).ConfigureAwait(false);
            if (stillResolved != null)
            {
                return new DeleteMethodResult(
                    "failed",
                    changedFiles,
                    targetMethodSymbolId,
                    null,
                    diagnosticsDelta,
                    RefactoringOperationExtensions.CreateError(
                        ErrorCodes.InternalError,
                        "The deleted method still resolves after mutation.",
                        ("targetMethodSymbolId", targetMethodSymbolId),
                        ("operation", "delete_method")));
            }

            var (applyVersion, versionError) = await _owner._solutionAccessor.GetWorkspaceVersionAsync(ct).ConfigureAwait(false);
            if (versionError != null)
            {
                return RefactoringOperationExtensions.CreateDeleteMethodErrorResult(targetMethodSymbolId, versionError);
            }

            if (applyVersion != version)
            {
                return new DeleteMethodResult(
                    "failed",
                    Array.Empty<string>(),
                    targetMethodSymbolId,
                    null,
                    new DiagnosticsDeltaInfo(Array.Empty<MutationDiagnosticInfo>(), Array.Empty<MutationDiagnosticInfo>()),
                    RefactoringOperationExtensions.CreateError(
                        ErrorCodes.WorkspaceChanged,
                        "Workspace changed during delete_method execution.",
                        ("targetMethodSymbolId", targetMethodSymbolId),
                        ("operation", "delete_method")));
            }

            var (applied, applyError) = await _owner._solutionAccessor.TryApplySolutionAsync(updatedSolution, ct).ConfigureAwait(false);
            if (!applied)
            {
                return new DeleteMethodResult(
                    "failed",
                    changedFiles,
                    targetMethodSymbolId,
                    null,
                    diagnosticsDelta,
                    applyError ?? RefactoringOperationExtensions.CreateError(
                        ErrorCodes.InternalError,
                        "Failed to apply delete_method changes.",
                        ("targetMethodSymbolId", targetMethodSymbolId),
                        ("operation", "delete_method")));
            }

            return new DeleteMethodResult(
                "applied",
                changedFiles,
                targetMethodSymbolId,
                deletedMethod,
                diagnosticsDelta);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _owner._logger.LogError(ex, "DeleteMethod failed for {TargetMethodSymbolId}", request.TargetMethodSymbolId);
            return RefactoringOperationExtensions.CreateDeleteMethodErrorResult(
                targetMethodSymbolId,
                ErrorCodes.InternalError,
                $"Failed to delete method '{request.TargetMethodSymbolId}': {ex.Message}",
                ("targetMethodSymbolId", targetMethodSymbolId),
                ("operation", "delete_method"));
        }
    }
}
