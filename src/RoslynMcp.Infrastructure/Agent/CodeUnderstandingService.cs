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

    public CodeUnderstandingService(
        IRoslynSolutionAccessor solutionAccessor,
        ISolutionSessionService solutionSessionService,
        IWorkspaceBootstrapService workspaceBootstrapService,
        IAnalysisService analysisService,
        INavigationService navigationService,
        ISymbolLookupService symbolLookupService,
        ISymbolDocumentationProvider symbolDocumentationProvider)
    {
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
        => _understandProjectsHandler.HandleAsync(request, ct);

    public Task<ExplainSymbolResult> ExplainSymbolAsync(ExplainSymbolRequest request, CancellationToken ct)
        => _explainSymbolHandler.HandleAsync(request, ct);

    public Task<ListTypesResult> ListTypesAsync(ListTypesRequest request, CancellationToken ct)
        => _listTypesHandler.HandleAsync(request, ct);

    public Task<ListMembersResult> ListMembersAsync(ListMembersRequest request, CancellationToken ct)
        => _listMembersHandler.HandleAsync(request, ct);

    public Task<ResolveSymbolResult> ResolveSymbolAsync(ResolveSymbolRequest request, CancellationToken ct)
        => _resolveSymbolHandler.HandleAsync(request, ct);

    public Task<ResolveSymbolsBatchResult> ResolveSymbolsBatchAsync(ResolveSymbolsBatchRequest request, CancellationToken ct)
        => _resolveSymbolsBatchHandler.HandleAsync(request, ct);
}
