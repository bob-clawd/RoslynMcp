using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Agent;

namespace RoslynMcp.Infrastructure.Refactoring;

internal sealed class ReplaceMethodOperations
{
    private readonly RefactoringOperationOrchestrator _owner;
    private readonly MethodDeclarationBuilder _builder;
    private readonly MethodMutationTargetResolver _targetResolver;
    private readonly DocumentDiagnosticsDeltaService _diagnosticsDeltaService;

    public ReplaceMethodOperations(RefactoringOperationOrchestrator owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _builder = new MethodDeclarationBuilder();
        _targetResolver = new MethodMutationTargetResolver(owner);
        _diagnosticsDeltaService = new DocumentDiagnosticsDeltaService();
    }

    public async Task<ReplaceMethodResult> ReplaceMethodAsync(ReplaceMethodRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var targetMethodSymbolId = request.TargetMethodSymbolId.NormalizeAcceptedSymbolIdForOutput();

        var validationError = request.ValidateReplaceMethod();
        if (validationError != null)
        {
            return validationError;
        }

        try
        {
            var (solution, version, error) = await _owner.TryGetSolutionWithVersionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return RefactoringOperationExtensions.CreateReplaceMethodErrorResult(targetMethodSymbolId, error);
            }

            var (target, resolveError) = await _targetResolver.ResolveMethodAsync(request.TargetMethodSymbolId, solution, "replace_method", ct).ConfigureAwait(false);
            if (target == null)
            {
                return RefactoringOperationExtensions.CreateReplaceMethodErrorResult(targetMethodSymbolId, resolveError);
            }

            if (!_builder.TryBuild(request.Method, out var replacementMethod, out var builderError) || replacementMethod == null)
            {
                return RefactoringOperationExtensions.CreateReplaceMethodErrorResult(targetMethodSymbolId, builderError);
            }

            if (MethodSignatureComparer.HasEquivalentMethod(target.MethodSymbol.ContainingType, request.Method, target.MethodSymbol))
            {
                return RefactoringOperationExtensions.CreateReplaceMethodErrorResult(
                    targetMethodSymbolId,
                    ErrorCodes.MethodConflict,
                    $"An equivalent method '{request.Method.Name}' already exists on '{target.MethodSymbol.ContainingType.ToDisplayString()}'.",
                    ("targetMethodSymbolId", targetMethodSymbolId),
                    ("methodName", request.Method.Name),
                    ("operation", "replace_method"));
            }

            var root = await target.Document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (root == null)
            {
                return RefactoringOperationExtensions.CreateReplaceMethodErrorResult(
                    targetMethodSymbolId,
                    ErrorCodes.InternalError,
                    "Failed to load the target document syntax tree.",
                    ("targetMethodSymbolId", targetMethodSymbolId),
                    ("operation", "replace_method"));
            }

            var updatedRoot = root.ReplaceNode(target.Declaration, replacementMethod.WithTriviaFrom(target.Declaration));
            var updatedDocument = target.Document.WithSyntaxRoot(updatedRoot);
            var updatedSolution = await _owner.FormatScopeAsync(updatedDocument.Project.Solution, [updatedDocument], ct).ConfigureAwait(false);

            var changedFiles = (await solution.CollectChangedFilesAsync(updatedSolution, ct).ConfigureAwait(false))
                .Select(static file => file.FilePath)
                .ToArray();

            var diagnosticsDelta = await _diagnosticsDeltaService
                .GetDeltaAsync(solution, updatedSolution, target.Document.Id, ct)
                .ConfigureAwait(false);

            var replacedMethod = await ResolveReplacedMethodAsync(updatedSolution, request, target.MethodSymbol, ct).ConfigureAwait(false);
            if (replacedMethod == null)
            {
                return new ReplaceMethodResult(
                    "failed",
                    changedFiles,
                    targetMethodSymbolId,
                    null,
                    diagnosticsDelta,
                    RefactoringOperationExtensions.CreateError(
                        ErrorCodes.CreatedSymbolUnresolved,
                        "The replaced method could not be resolved after mutation.",
                        ("targetMethodSymbolId", targetMethodSymbolId),
                        ("methodName", request.Method.Name),
                        ("operation", "replace_method")));
            }

            var (applyVersion, versionError) = await _owner._solutionAccessor.GetWorkspaceVersionAsync(ct).ConfigureAwait(false);
            if (versionError != null)
            {
                return RefactoringOperationExtensions.CreateReplaceMethodErrorResult(targetMethodSymbolId, versionError);
            }

            if (applyVersion != version)
            {
                return new ReplaceMethodResult(
                    "failed",
                    Array.Empty<string>(),
                    targetMethodSymbolId,
                    null,
                    new DiagnosticsDeltaInfo(Array.Empty<MutationDiagnosticInfo>(), Array.Empty<MutationDiagnosticInfo>()),
                    RefactoringOperationExtensions.CreateError(
                        ErrorCodes.WorkspaceChanged,
                        "Workspace changed during replace_method execution.",
                        ("targetMethodSymbolId", targetMethodSymbolId),
                        ("operation", "replace_method")));
            }

            var (applied, applyError) = await _owner._solutionAccessor.TryApplySolutionAsync(updatedSolution, ct).ConfigureAwait(false);
            if (!applied)
            {
                return new ReplaceMethodResult(
                    "failed",
                    changedFiles,
                    targetMethodSymbolId,
                    null,
                    diagnosticsDelta,
                    applyError ?? RefactoringOperationExtensions.CreateError(
                        ErrorCodes.InternalError,
                        "Failed to apply replace_method changes.",
                        ("targetMethodSymbolId", targetMethodSymbolId),
                        ("operation", "replace_method")));
            }

            return new ReplaceMethodResult(
                "applied",
                changedFiles,
                targetMethodSymbolId,
                replacedMethod,
                diagnosticsDelta);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _owner._logger.LogError(ex, "ReplaceMethod failed for {TargetMethodSymbolId}", request.TargetMethodSymbolId);
            return RefactoringOperationExtensions.CreateReplaceMethodErrorResult(
                targetMethodSymbolId,
                ErrorCodes.InternalError,
                $"Failed to replace method '{request.TargetMethodSymbolId}': {ex.Message}",
                ("targetMethodSymbolId", targetMethodSymbolId),
                ("operation", "replace_method"));
        }
    }

    private async Task<ReplacedMethodInfo?> ResolveReplacedMethodAsync(
        Solution updatedSolution,
        ReplaceMethodRequest request,
        IMethodSymbol originalMethod,
        CancellationToken ct)
    {
        var containingType = await _owner.ResolveSymbolAsync(RefactoringSymbolIdentity.CreateId(originalMethod.ContainingType), updatedSolution, ct).ConfigureAwait(false) as INamedTypeSymbol;
        if (containingType == null)
        {
            return null;
        }

        var method = containingType.GetMembers(request.Method.Name)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(candidate => MethodSignatureComparer.MatchesMethodSignature(candidate, request.Method));
        if (method == null)
        {
            return null;
        }

        return new ReplacedMethodInfo(
            request.TargetMethodSymbolId.NormalizeAcceptedSymbolIdForOutput(),
            originalMethod.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            RefactoringSymbolIdentity.CreateId(method).ToExternal(),
            method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }
}
