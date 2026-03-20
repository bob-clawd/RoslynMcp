using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Workspace;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Infrastructure.Refactoring;

/// <summary>
/// Public facade for refactoring operations: get fixes, apply refactorings, run cleanup.
/// Wraps the refactoring operation orchestrator.
/// </summary>
public sealed class RoslynRefactoringService : IRefactoringService
{
    private readonly IRefactoringOperationOrchestrator _orchestrator;
    private readonly string _workspaceRoot;

    internal RoslynRefactoringService(IRefactoringOperationOrchestrator orchestrator, ICurrentWorkspaceRootProvider currentWorkspaceRootProvider)
    {
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _workspaceRoot = currentWorkspaceRootProvider?.WorkspaceRoot ?? throw new ArgumentNullException(nameof(currentWorkspaceRootProvider));
    }

    public RoslynRefactoringService(IRoslynSolutionAccessor solutionAccessor, ICurrentWorkspaceRootProvider currentWorkspaceRootProvider, ILogger<RoslynRefactoringService>? logger = null)
        : this(new RefactoringOperationOrchestrator(solutionAccessor, currentWorkspaceRootProvider, logger), currentWorkspaceRootProvider)
    { }

    public Task<GetRefactoringsAtPositionResult> GetRefactoringsAtPositionAsync(GetRefactoringsAtPositionRequest request, CancellationToken ct)
        => _orchestrator.GetRefactoringsAtPositionAsync(request.WithWorkspaceAbsolutePaths(_workspaceRoot), ct);

    public Task<PreviewRefactoringResult> PreviewRefactoringAsync(PreviewRefactoringRequest request, CancellationToken ct)
        => _orchestrator.PreviewRefactoringAsync(request, ct);

    public Task<ApplyRefactoringResult> ApplyRefactoringAsync(ApplyRefactoringRequest request, CancellationToken ct)
        => _orchestrator.ApplyRefactoringAsync(request, ct);

    public Task<GetCodeFixesResult> GetCodeFixesAsync(GetCodeFixesRequest request, CancellationToken ct)
        => _orchestrator.GetCodeFixesAsync(request, ct);

    public Task<PreviewCodeFixResult> PreviewCodeFixAsync(PreviewCodeFixRequest request, CancellationToken ct)
        => _orchestrator.PreviewCodeFixAsync(request, ct);

    public Task<ApplyCodeFixResult> ApplyCodeFixAsync(ApplyCodeFixRequest request, CancellationToken ct)
        => _orchestrator.ApplyCodeFixAsync(request, ct);

    public Task<ExecuteCleanupResult> ExecuteCleanupAsync(ExecuteCleanupRequest request, CancellationToken ct)
        => _orchestrator.ExecuteCleanupAsync(request, ct);

    public Task<RenameSymbolResult> RenameSymbolAsync(RenameSymbolRequest request, CancellationToken ct)
        => HandleAsync(() => _orchestrator.RenameSymbolAsync(request, ct), static (result, workspaceRoot) => result.WithWorkspaceRelativePaths(workspaceRoot));

    public Task<FormatDocumentResult> FormatDocumentAsync(FormatDocumentRequest request, CancellationToken ct)
        => HandleAsync(() => _orchestrator.FormatDocumentAsync(request.WithWorkspaceAbsolutePaths(_workspaceRoot), ct), static (result, workspaceRoot) => result.WithWorkspaceRelativePaths(workspaceRoot));

    public Task<AddMethodResult> AddMethodAsync(AddMethodRequest request, CancellationToken ct)
        => HandleAsync(() => _orchestrator.AddMethodAsync(request, ct), static (result, workspaceRoot) => result.WithWorkspaceRelativePaths(workspaceRoot));

    public Task<DeleteMethodResult> DeleteMethodAsync(DeleteMethodRequest request, CancellationToken ct)
        => HandleAsync(() => _orchestrator.DeleteMethodAsync(request, ct), static (result, workspaceRoot) => result.WithWorkspaceRelativePaths(workspaceRoot));

    public Task<ReplaceMethodResult> ReplaceMethodAsync(ReplaceMethodRequest request, CancellationToken ct)
        => HandleAsync(() => _orchestrator.ReplaceMethodAsync(request, ct), static (result, workspaceRoot) => result.WithWorkspaceRelativePaths(workspaceRoot));

    public Task<ReplaceMethodBodyResult> ReplaceMethodBodyAsync(ReplaceMethodBodyRequest request, CancellationToken ct)
        => HandleAsync(() => _orchestrator.ReplaceMethodBodyAsync(request, ct), static (result, workspaceRoot) => result.WithWorkspaceRelativePaths(workspaceRoot));

    private async Task<TResult> HandleAsync<TResult>(Func<Task<TResult>> action, Func<TResult, string, TResult> shape)
    {
        var result = await action().ConfigureAwait(false);
        return shape(result, _workspaceRoot);
    }
}
