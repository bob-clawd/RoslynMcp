using RoslynMcp.Core;
using RoslynMcp.Core.Models.Navigation;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Infrastructure.Navigation;

internal sealed class NavigationTypeHierarchyService
{
    private const int DefaultMaxDerived = 200;
    private const int MaxOutlineDepth = 3;

    private readonly NavigationSolutionProvider _solutionProvider;
    private readonly ISymbolLookupService _symbolLookupService;
    private readonly ITypeIntrospectionService _typeIntrospectionService;
    private readonly ILogger _logger;

    public NavigationTypeHierarchyService(
        NavigationSolutionProvider solutionProvider,
        ISymbolLookupService symbolLookupService,
        ITypeIntrospectionService typeIntrospectionService,
        ILogger logger)
    {
        _solutionProvider = solutionProvider ?? throw new ArgumentNullException(nameof(solutionProvider));
        _symbolLookupService = symbolLookupService ?? throw new ArgumentNullException(nameof(symbolLookupService));
        _typeIntrospectionService = typeIntrospectionService ?? throw new ArgumentNullException(nameof(typeIntrospectionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GetTypeHierarchyResult> GetTypeHierarchyAsync(GetTypeHierarchyRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidSymbolIdError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "get-type-hierarchy");
        if (invalidSymbolIdError != null)
        {
            return new GetTypeHierarchyResult(null,
                Array.Empty<SymbolDescriptor>(),
                Array.Empty<SymbolDescriptor>(),
                Array.Empty<SymbolDescriptor>(),
                invalidSymbolIdError);
        }

        try
        {
            var (solution, error) = await _solutionProvider.TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new GetTypeHierarchyResult(null,
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<SymbolDescriptor>(),
                    error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new GetTypeHierarchyResult(null,
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<SymbolDescriptor>(),
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "get-type-hierarchy")));
            }

            var typeSymbol = _typeIntrospectionService.GetRelatedType(symbol);
            if (typeSymbol == null)
            {
                return new GetTypeHierarchyResult(null,
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<SymbolDescriptor>(),
                    Array.Empty<SymbolDescriptor>(),
                    NavigationErrorFactory.CreateError(ErrorCodes.InvalidRequest,
                        "symbolId must resolve to a type or a member declared on a type.",
                        ("parameter", "symbolId"),
                        ("operation", "get-type-hierarchy")));
            }

            var includeTransitive = request.IncludeTransitive ?? true;
            var maxDerived = Math.Max(request.MaxDerived ?? DefaultMaxDerived, 0);
            var baseTypes = _typeIntrospectionService.CollectBaseTypes(typeSymbol, includeTransitive);
            var interfaces = _typeIntrospectionService.CollectImplementedInterfaces(typeSymbol, includeTransitive);
            var derived = await _typeIntrospectionService
                .CollectDerivedTypesAsync(typeSymbol, solution, includeTransitive, maxDerived, ct)
                .ConfigureAwait(false);

            return new GetTypeHierarchyResult(
                NavigationModelUtilities.CreateDescriptor(typeSymbol),
                baseTypes,
                interfaces,
                derived);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTypeHierarchy failed for {SymbolId}", request.SymbolId);
            return new GetTypeHierarchyResult(null,
                Array.Empty<SymbolDescriptor>(),
                Array.Empty<SymbolDescriptor>(),
                Array.Empty<SymbolDescriptor>(),
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to compute type hierarchy '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "get-type-hierarchy")));
        }
    }

    public async Task<GetSymbolOutlineResult> GetSymbolOutlineAsync(GetSymbolOutlineRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidSymbolIdError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "get-symbol-outline");
        if (invalidSymbolIdError != null)
        {
            return new GetSymbolOutlineResult(null, Array.Empty<SymbolMemberOutline>(), Array.Empty<string>(), invalidSymbolIdError);
        }

        try
        {
            var (solution, error) = await _solutionProvider.TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new GetSymbolOutlineResult(null, Array.Empty<SymbolMemberOutline>(), Array.Empty<string>(), error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new GetSymbolOutlineResult(null,
                    Array.Empty<SymbolMemberOutline>(),
                    Array.Empty<string>(),
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "get-symbol-outline")));
            }

            var depth = Math.Clamp(request.Depth ?? 1, 1, MaxOutlineDepth);
            var members = _typeIntrospectionService.CollectOutlineMembers(symbol, depth);
            var attributes = _typeIntrospectionService.CollectAttributes(symbol);
            return new GetSymbolOutlineResult(NavigationModelUtilities.CreateDescriptor(symbol), members, attributes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetSymbolOutline failed for {SymbolId}", request.SymbolId);
            return new GetSymbolOutlineResult(null,
                Array.Empty<SymbolMemberOutline>(),
                Array.Empty<string>(),
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to build symbol outline '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "get-symbol-outline")));
        }
    }
}
