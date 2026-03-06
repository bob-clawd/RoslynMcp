using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Infrastructure.Navigation;

internal sealed class NavigationReferenceQueryService
{
    private readonly NavigationSolutionProvider _solutionProvider;
    private readonly ISymbolLookupService _symbolLookupService;
    private readonly IReferenceSearchService _referenceSearchService;
    private readonly ILogger _logger;

    public NavigationReferenceQueryService(
        NavigationSolutionProvider solutionProvider,
        ISymbolLookupService symbolLookupService,
        IReferenceSearchService referenceSearchService,
        ILogger logger)
    {
        _solutionProvider = solutionProvider ?? throw new ArgumentNullException(nameof(solutionProvider));
        _symbolLookupService = symbolLookupService ?? throw new ArgumentNullException(nameof(symbolLookupService));
        _referenceSearchService = referenceSearchService ?? throw new ArgumentNullException(nameof(referenceSearchService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FindReferencesResult> FindReferencesAsync(FindReferencesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var scopedResult = await FindReferencesCoreAsync(
            request.SymbolId,
            ReferenceScopes.Solution,
            null,
            operation: "find-references",
            ct).ConfigureAwait(false);

        return new FindReferencesResult(scopedResult.Symbol, scopedResult.References, scopedResult.Error);
    }

    public async Task<FindReferencesScopedResult> FindReferencesScopedAsync(FindReferencesScopedRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        return await FindReferencesCoreAsync(
            request.SymbolId,
            request.Scope,
            request.Path,
            operation: "find-references-scoped",
            ct).ConfigureAwait(false);
    }

    public async Task<FindImplementationsResult> FindImplementationsAsync(FindImplementationsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidInputError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "find-implementations");
        if (invalidInputError != null)
        {
            return new FindImplementationsResult(null, Array.Empty<SymbolDescriptor>(), invalidInputError);
        }

        try
        {
            var (solution, error) = await _solutionProvider.TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new FindImplementationsResult(null, Array.Empty<SymbolDescriptor>(), error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new FindImplementationsResult(null,
                    Array.Empty<SymbolDescriptor>(),
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "find-implementations")));
            }

            var implementations = await _referenceSearchService.FindImplementationsAsync(symbol, solution, ct).ConfigureAwait(false);
            return new FindImplementationsResult(symbol.ToSymbolDescriptor(), implementations);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FindImplementations failed for {SymbolId}", request.SymbolId);
            return new FindImplementationsResult(null,
                Array.Empty<SymbolDescriptor>(),
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to find implementations '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "find-implementations")));
        }
    }

    private async Task<FindReferencesScopedResult> FindReferencesCoreAsync(
        string symbolId,
        string scope,
        string? path,
        string operation,
        CancellationToken ct)
    {
        var invalidSymbolIdError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(symbolId, operation);
        if (invalidSymbolIdError != null)
        {
            return new FindReferencesScopedResult(null, Array.Empty<SourceLocation>(), 0, invalidSymbolIdError);
        }

        if (!_referenceSearchService.IsValidScope(scope))
        {
            return new FindReferencesScopedResult(null,
                Array.Empty<SourceLocation>(),
                0,
                NavigationErrorFactory.CreateError(ErrorCodes.InvalidRequest,
                    "scope must be one of: document, project, solution.",
                    ("parameter", "scope"),
                    ("operation", operation)));
        }

        if (string.Equals(scope, ReferenceScopes.Document, StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(path))
        {
            return new FindReferencesScopedResult(null,
                Array.Empty<SourceLocation>(),
                0,
                NavigationErrorFactory.CreateError(ErrorCodes.InvalidRequest,
                    "path is required when scope is document.",
                    ("parameter", "path"),
                    ("operation", operation)));
        }

        try
        {
            var (solution, error) = await _solutionProvider.TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new FindReferencesScopedResult(null, Array.Empty<SourceLocation>(), 0, error);
            }

            var (symbol, ownerProject) = await _symbolLookupService.ResolveSymbolWithProjectAsync(symbolId, solution, ct)
                .ConfigureAwait(false);
            if (symbol == null)
            {
                return new FindReferencesScopedResult(null,
                    Array.Empty<SourceLocation>(),
                    0,
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{symbolId}' could not be resolved.",
                        ("symbolId", symbolId),
                        ("operation", operation)));
            }

            if (string.Equals(scope, ReferenceScopes.Document, StringComparison.Ordinal))
            {
                var pathError = _referenceSearchService.TryValidateDocumentPath(path!, solution);
                if (pathError != null)
                {
                    return new FindReferencesScopedResult(null, Array.Empty<SourceLocation>(), 0, pathError);
                }
            }

            var references = await _referenceSearchService
                .FindReferencesScopedAsync(symbol, solution, scope, path, ownerProject, ct)
                .ConfigureAwait(false);

            return new FindReferencesScopedResult(
                symbol.ToSymbolDescriptor(),
                references,
                references.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FindReferencesScoped failed for {SymbolId}", symbolId);
            return new FindReferencesScopedResult(null,
                Array.Empty<SourceLocation>(),
                0,
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to find references '{symbolId}': {ex.Message}",
                    ("symbolId", symbolId),
                    ("operation", operation)));
        }
    }
}
