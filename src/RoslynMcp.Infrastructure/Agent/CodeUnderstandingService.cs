using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Agent.Handlers;
using RoslynMcp.Infrastructure.Documentation;
using RoslynMcp.Infrastructure.Navigation;
using RoslynMcp.Infrastructure.Workspace;

namespace RoslynMcp.Infrastructure.Agent;

/// <summary>
/// Central orchestration for code understanding operations.
/// Aggregates handlers for project understanding, symbol discovery, and inspections.
/// </summary>
internal sealed class CodeUnderstandingService : ICodeUnderstandingService
{
    private readonly UnderstandProjectsHandler _understandProjectsHandler;
    private readonly ListTypesHandler _listTypesHandler;
    private readonly ListMembersHandler _listMembersHandler;
    private readonly ResolveSymbolHandler _resolveSymbolHandler;
    private readonly ResolveSymbolsBatchHandler _resolveSymbolsBatchHandler;
    private readonly ExplainSymbolHandler _explainSymbolHandler;
    private readonly string _workspaceRoot;

    public CodeUnderstandingService(
        IRoslynSolutionAccessor solutionAccessor,
        ISolutionSessionService solutionSessionService,
        IWorkspaceBootstrapService workspaceBootstrapService,
        IAnalysisService analysisService,
        INavigationService navigationService,
        ISymbolLookupService symbolLookupService,
        ISymbolDocumentationProvider symbolDocumentationProvider,
        ICurrentWorkspaceRootProvider currentWorkspaceRootProvider)
    {
        _workspaceRoot = currentWorkspaceRootProvider?.WorkspaceRoot ?? throw new ArgumentNullException(nameof(currentWorkspaceRootProvider));
        var queries = new CodeUnderstandingQueryService(
            solutionAccessor,
            solutionSessionService,
            workspaceBootstrapService,
            symbolLookupService,
            navigationService);

        _understandProjectsHandler = new UnderstandProjectsHandler(queries, analysisService);
        _listTypesHandler = new ListTypesHandler(queries, symbolDocumentationProvider);
        _listMembersHandler = new ListMembersHandler(queries);
        _resolveSymbolHandler = new ResolveSymbolHandler(queries, symbolLookupService);
        _resolveSymbolsBatchHandler = new ResolveSymbolsBatchHandler(_resolveSymbolHandler);
        _explainSymbolHandler = new ExplainSymbolHandler(queries, navigationService, solutionAccessor, symbolLookupService, symbolDocumentationProvider);
    }

    public Task<UnderstandProjectsResult> UnderstandProjectsAsync(UnderstandProjectsRequest request, CancellationToken ct)
        => HandleAsync(() => _understandProjectsHandler.HandleAsync(request, ct), static (result, workspaceRoot) => result.WithWorkspaceRelativePaths(workspaceRoot));

    public Task<ExplainSymbolResult> ExplainSymbolAsync(ExplainSymbolRequest request, CancellationToken ct)
        => HandleAsync(() => _explainSymbolHandler.HandleAsync(request.WithWorkspaceAbsolutePaths(_workspaceRoot), ct), static (result, workspaceRoot) => result.WithWorkspaceRelativePaths(workspaceRoot));

    public Task<ListTypesResult> ListTypesAsync(ListTypesRequest request, CancellationToken ct)
        => HandleAsync(() => _listTypesHandler.HandleAsync(request.WithWorkspaceAbsolutePaths(_workspaceRoot), ct), static (result, workspaceRoot) => result.WithWorkspaceRelativePaths(workspaceRoot));

    public Task<ListMembersResult> ListMembersAsync(ListMembersRequest request, CancellationToken ct)
        => HandleAsync(() => _listMembersHandler.HandleAsync(request.WithWorkspaceAbsolutePaths(_workspaceRoot), ct), static (result, workspaceRoot) => result.WithWorkspaceRelativePaths(workspaceRoot));

    public Task<ResolveSymbolResult> ResolveSymbolAsync(ResolveSymbolRequest request, CancellationToken ct)
        => HandleAsync(() => _resolveSymbolHandler.HandleAsync(request.WithWorkspaceAbsolutePaths(_workspaceRoot), ct), static (result, workspaceRoot) => result.WithWorkspaceRelativePaths(workspaceRoot));

    public Task<ResolveSymbolsBatchResult> ResolveSymbolsBatchAsync(ResolveSymbolsBatchRequest request, CancellationToken ct)
        => HandleAsync(() => _resolveSymbolsBatchHandler.HandleAsync(request.WithWorkspaceAbsolutePaths(_workspaceRoot), ct), static (result, workspaceRoot) => result.WithWorkspaceRelativePaths(workspaceRoot));

    private async Task<TResult> HandleAsync<TResult>(Func<Task<TResult>> action, Func<TResult, string, TResult> shape)
    {
        var result = await action().ConfigureAwait(false);
        return shape(result, _workspaceRoot);
    }
}
