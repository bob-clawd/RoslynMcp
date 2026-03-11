using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Agent;

namespace RoslynMcp.Infrastructure.Refactoring;

internal sealed class ReplaceMethodBodyOperations
{
    private readonly RefactoringOperationOrchestrator _owner;
    private readonly MethodDeclarationBuilder _builder;
    private readonly MethodMutationTargetResolver _targetResolver;
    private readonly DocumentDiagnosticsDeltaService _diagnosticsDeltaService;

    public ReplaceMethodBodyOperations(RefactoringOperationOrchestrator owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _builder = new MethodDeclarationBuilder();
        _targetResolver = new MethodMutationTargetResolver(owner);
        _diagnosticsDeltaService = new DocumentDiagnosticsDeltaService();
    }

    public async Task<ReplaceMethodBodyResult> ReplaceMethodBodyAsync(ReplaceMethodBodyRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var targetInternalSymbolId = RefactoringOperationOrchestrator.NormalizeInputSymbolId(request.TargetMethodSymbolId)
            ?? request.TargetMethodSymbolId;
        var targetMethodSymbolId = targetInternalSymbolId.NormalizeAcceptedSymbolIdForOutput();

        var validationError = request.ValidateReplaceMethodBody();
        if (validationError != null)
        {
            return validationError;
        }

        try
        {
            var (solution, version, error) = await _owner.TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return RefactoringOperationExtensions.CreateReplaceMethodBodyErrorResult(targetMethodSymbolId, error);
            }

            var (target, resolveError) = await _targetResolver.ResolveMethodAsync(request.TargetMethodSymbolId, solution, "replace_method_body", ct).ConfigureAwait(false);
            if (target == null)
            {
                return RefactoringOperationExtensions.CreateReplaceMethodBodyErrorResult(targetMethodSymbolId, resolveError);
            }

            if (target.Declaration.Body == null || target.Declaration.ExpressionBody != null)
            {
                return RefactoringOperationExtensions.CreateReplaceMethodBodyErrorResult(
                    targetMethodSymbolId,
                    ErrorCodes.TargetNotSourceEditable,
                    "replace_method_body currently supports only existing block-bodied methods.",
                    ("targetMethodSymbolId", targetMethodSymbolId),
                    ("operation", "replace_method_body"));
            }

            if (!_builder.TryParseBody(request.Body, out var bodyBlock, out var bodyError) || bodyBlock == null)
            {
                return RefactoringOperationExtensions.CreateReplaceMethodBodyErrorResult(targetMethodSymbolId, bodyError);
            }

            var root = await target.Document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (root == null)
            {
                return RefactoringOperationExtensions.CreateReplaceMethodBodyErrorResult(
                    targetMethodSymbolId,
                    ErrorCodes.InternalError,
                    "Failed to load the target document syntax tree.",
                    ("targetMethodSymbolId", targetMethodSymbolId),
                    ("operation", "replace_method_body"));
            }

            var updatedMethod = target.Declaration.WithBody(bodyBlock.WithTriviaFrom(target.Declaration.Body));
            var updatedRoot = root.ReplaceNode(target.Declaration, updatedMethod);
            var updatedDocument = target.Document.WithSyntaxRoot(updatedRoot);
            var updatedSolution = await _owner.FormatScopeAsync(updatedDocument.Project.Solution, [updatedDocument], ct).ConfigureAwait(false);

            var changedFiles = (await solution.CollectChangedFilesAsync(updatedSolution, ct).ConfigureAwait(false))
                .Select(static file => file.FilePath)
                .ToArray();

            var diagnosticsDelta = await _diagnosticsDeltaService
                .GetDeltaAsync(solution, updatedSolution, target.Document.Id, ct)
                .ConfigureAwait(false);

            var (applyVersion, versionError) = await _owner._solutionAccessor.GetWorkspaceVersionAsync(ct).ConfigureAwait(false);
            if (versionError != null)
            {
                return RefactoringOperationExtensions.CreateReplaceMethodBodyErrorResult(targetMethodSymbolId, versionError);
            }

            if (applyVersion != version)
            {
                return new ReplaceMethodBodyResult(
                    "failed",
                    Array.Empty<string>(),
                    targetMethodSymbolId,
                    null,
                    new DiagnosticsDeltaInfo(Array.Empty<MutationDiagnosticInfo>(), Array.Empty<MutationDiagnosticInfo>()),
                    RefactoringOperationExtensions.CreateError(
                        ErrorCodes.WorkspaceChanged,
                        "Workspace changed during replace_method_body execution.",
                        ("targetMethodSymbolId", targetMethodSymbolId),
                        ("operation", "replace_method_body")));
            }

            var (applied, applyError) = await _owner._solutionAccessor.TryApplySolutionAsync(updatedSolution, ct).ConfigureAwait(false);
            if (!applied)
            {
                return new ReplaceMethodBodyResult(
                    "failed",
                    changedFiles,
                    targetMethodSymbolId,
                    null,
                    diagnosticsDelta,
                    applyError ?? RefactoringOperationExtensions.CreateError(
                        ErrorCodes.InternalError,
                        "Failed to apply replace_method_body changes.",
                        ("targetMethodSymbolId", targetMethodSymbolId),
                        ("operation", "replace_method_body")));
            }

            var resolvedMethod = await _owner.ResolveSymbolAsync(request.TargetMethodSymbolId, updatedSolution, ct).ConfigureAwait(false) as IMethodSymbol;
            if (resolvedMethod == null)
            {
                return new ReplaceMethodBodyResult(
                    "failed",
                    changedFiles,
                    targetMethodSymbolId,
                    null,
                    diagnosticsDelta,
                    RefactoringOperationExtensions.CreateError(
                        ErrorCodes.CreatedSymbolUnresolved,
                        "The method could not be resolved after replacing its body.",
                        ("targetMethodSymbolId", targetMethodSymbolId),
                        ("operation", "replace_method_body")));
            }

            var resolvedMethodInternalId = RefactoringSymbolIdentity.CreateId(resolvedMethod);
            targetInternalSymbolId.Update(resolvedMethodInternalId);

            return new ReplaceMethodBodyResult(
                "applied",
                changedFiles,
                targetMethodSymbolId,
                new ReplacedMethodBodyInfo(
                    targetMethodSymbolId,
                    resolvedMethod.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)),
                diagnosticsDelta);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _owner._logger.LogError(ex, "ReplaceMethodBody failed for {TargetMethodSymbolId}", request.TargetMethodSymbolId);
            return RefactoringOperationExtensions.CreateReplaceMethodBodyErrorResult(
                targetMethodSymbolId,
                ErrorCodes.InternalError,
                $"Failed to replace method body '{request.TargetMethodSymbolId}': {ex.Message}",
                ("targetMethodSymbolId", targetMethodSymbolId),
                ("operation", "replace_method_body"));
        }
    }
}
