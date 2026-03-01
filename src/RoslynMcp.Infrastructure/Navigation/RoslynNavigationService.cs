using RoslynMcp.Core.Contracts;
using RoslynMcp.Core;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Navigation;
using RoslynMcp.Infrastructure.Workspace;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RoslynMcp.Infrastructure.Navigation;

public sealed class RoslynNavigationService : INavigationService
{
    private readonly NavigationSymbolQueryService _symbolQueries;
    private readonly NavigationReferenceQueryService _referenceQueries;
    private readonly NavigationTypeHierarchyService _typeHierarchyQueries;
    private readonly NavigationCallGraphQueryService _callGraphQueries;

    public RoslynNavigationService(IRoslynSolutionAccessor solutionAccessor,
        ISymbolLookupService symbolLookupService,
        IReferenceSearchService referenceSearchService,
        ICallGraphService callGraphService,
        ITypeIntrospectionService typeIntrospectionService,
        ILogger<RoslynNavigationService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(solutionAccessor);
        ArgumentNullException.ThrowIfNull(symbolLookupService);
        ArgumentNullException.ThrowIfNull(referenceSearchService);
        ArgumentNullException.ThrowIfNull(callGraphService);
        ArgumentNullException.ThrowIfNull(typeIntrospectionService);

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
        => _referenceQueries.FindReferencesScopedAsync(request, ct);

    public Task<FindImplementationsResult> FindImplementationsAsync(FindImplementationsRequest request, CancellationToken ct)
        => _referenceQueries.FindImplementationsAsync(request, ct);

    public Task<GetTypeHierarchyResult> GetTypeHierarchyAsync(GetTypeHierarchyRequest request, CancellationToken ct)
        => _typeHierarchyQueries.GetTypeHierarchyAsync(request, ct);

    public Task<GetSymbolOutlineResult> GetSymbolOutlineAsync(GetSymbolOutlineRequest request, CancellationToken ct)
        => _typeHierarchyQueries.GetSymbolOutlineAsync(request, ct);

    public Task<GetCallersResult> GetCallersAsync(GetCallersRequest request, CancellationToken ct)
        => _callGraphQueries.GetCallersAsync(request, ct);

    public Task<GetCalleesResult> GetCalleesAsync(GetCalleesRequest request, CancellationToken ct)
        => _callGraphQueries.GetCalleesAsync(request, ct);

    public Task<GetCallGraphResult> GetCallGraphAsync(GetCallGraphRequest request, CancellationToken ct)
        => _callGraphQueries.GetCallGraphAsync(request, ct);
}
