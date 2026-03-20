using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RoslynMcp.Infrastructure.Navigation;

/// <summary>
/// Central navigation service: orchestrates symbol resolution, references, call graph, and type hierarchy.
/// Aggregates all navigation queries under one interface.
/// </summary>
public sealed class RoslynNavigationService : INavigationService
{
    private readonly NavigationSymbolQueryService _symbolQueries;
    private readonly NavigationReferenceQueryService _referenceQueries;
    private readonly NavigationTypeHierarchyService _typeHierarchyQueries;
    private readonly NavigationCallGraphQueryService _callGraphQueries;
    private readonly string _workspaceRoot;

    public RoslynNavigationService(IRoslynSolutionAccessor solutionAccessor,
        ISymbolLookupService symbolLookupService,
        IReferenceSearchService referenceSearchService,
        ICallGraphService callGraphService,
        ITypeIntrospectionService typeIntrospectionService,
        ICurrentWorkspaceRootProvider currentWorkspaceRootProvider,
        ILogger<RoslynNavigationService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(solutionAccessor);
        ArgumentNullException.ThrowIfNull(symbolLookupService);
        ArgumentNullException.ThrowIfNull(referenceSearchService);
        ArgumentNullException.ThrowIfNull(callGraphService);
        ArgumentNullException.ThrowIfNull(typeIntrospectionService);
        _workspaceRoot = currentWorkspaceRootProvider?.WorkspaceRoot ?? throw new ArgumentNullException(nameof(currentWorkspaceRootProvider));

        var safeLogger = logger ?? NullLogger<RoslynNavigationService>.Instance;
        var solutionProvider = new NavigationSolutionProvider(solutionAccessor, safeLogger);
        _symbolQueries = new NavigationSymbolQueryService(solutionProvider, symbolLookupService, safeLogger);
        _referenceQueries = new NavigationReferenceQueryService(solutionProvider, symbolLookupService, referenceSearchService, safeLogger);
        _typeHierarchyQueries = new NavigationTypeHierarchyService(solutionProvider, symbolLookupService, typeIntrospectionService, safeLogger);
        _callGraphQueries = new NavigationCallGraphQueryService(solutionProvider, symbolLookupService, callGraphService, safeLogger);
    }

    public Task<FindSymbolResult> FindSymbolAsync(FindSymbolRequest request, CancellationToken ct)
        => _symbolQueries.FindSymbolAsync(request, ct);

    public Task<GetSymbolAtPositionResult> GetSymbolAtPositionAsync(GetSymbolAtPositionRequest request, CancellationToken ct)
        => _symbolQueries.GetSymbolAtPositionAsync(request, ct);

    public Task<SearchSymbolsResult> SearchSymbolsAsync(SearchSymbolsRequest request, CancellationToken ct)
        => _symbolQueries.SearchSymbolsAsync(request, ct);

    public Task<SearchSymbolsScopedResult> SearchSymbolsScopedAsync(SearchSymbolsScopedRequest request, CancellationToken ct)
        => _symbolQueries.SearchSymbolsScopedAsync(request, ct);

    public Task<GetSignatureResult> GetSignatureAsync(GetSignatureRequest request, CancellationToken ct)
        => _symbolQueries.GetSignatureAsync(request, ct);

    public Task<FindReferencesResult> FindReferencesAsync(FindReferencesRequest request, CancellationToken ct)
        => _referenceQueries.FindReferencesAsync(request, ct);

    public Task<FindReferencesScopedResult> FindReferencesScopedAsync(FindReferencesScopedRequest request, CancellationToken ct)
        => HandleAsync(() => _referenceQueries.FindReferencesScopedAsync(request.WithWorkspaceAbsolutePaths(_workspaceRoot), ct), static (result, workspaceRoot) => result.WithWorkspaceRelativePaths(workspaceRoot));

    public Task<FindImplementationsResult> FindImplementationsAsync(FindImplementationsRequest request, CancellationToken ct)
        => HandleAsync(() => _referenceQueries.FindImplementationsAsync(request, ct), static (result, workspaceRoot) => result.WithWorkspaceRelativePaths(workspaceRoot));

    public Task<GetTypeHierarchyResult> GetTypeHierarchyAsync(GetTypeHierarchyRequest request, CancellationToken ct)
        => HandleAsync(() => _typeHierarchyQueries.GetTypeHierarchyAsync(request, ct), static (result, workspaceRoot) => result.WithWorkspaceRelativePaths(workspaceRoot));

    public Task<GetSymbolOutlineResult> GetSymbolOutlineAsync(GetSymbolOutlineRequest request, CancellationToken ct)
        => _typeHierarchyQueries.GetSymbolOutlineAsync(request, ct);

    public Task<GetCallersResult> GetCallersAsync(GetCallersRequest request, CancellationToken ct)
        => _callGraphQueries.GetCallersAsync(request, ct);

    public Task<GetCalleesResult> GetCalleesAsync(GetCalleesRequest request, CancellationToken ct)
        => _callGraphQueries.GetCalleesAsync(request, ct);

    public Task<GetCallGraphResult> GetCallGraphAsync(GetCallGraphRequest request, CancellationToken ct)
        => _callGraphQueries.GetCallGraphAsync(request, ct);

    private async Task<TResult> HandleAsync<TResult>(Func<Task<TResult>> action, Func<TResult, string, TResult> shape)
    {
        var result = await action().ConfigureAwait(false);
        return shape(result, _workspaceRoot);
    }
}
