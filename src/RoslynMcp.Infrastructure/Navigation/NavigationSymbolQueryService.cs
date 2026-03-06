using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Infrastructure.Navigation;

internal sealed class NavigationSymbolQueryService
{
    private readonly NavigationSolutionProvider _solutionProvider;
    private readonly ISymbolLookupService _symbolLookupService;
    private readonly ILogger _logger;

    public NavigationSymbolQueryService(
        NavigationSolutionProvider solutionProvider,
        ISymbolLookupService symbolLookupService,
        ILogger logger)
    {
        _solutionProvider = solutionProvider ?? throw new ArgumentNullException(nameof(solutionProvider));
        _symbolLookupService = symbolLookupService ?? throw new ArgumentNullException(nameof(symbolLookupService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<FindSymbolResult> FindSymbolAsync(FindSymbolRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidInputError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "find-symbol");
        if (invalidInputError != null)
        {
            return new FindSymbolResult(null, invalidInputError);
        }

        try
        {
            var (solution, error) = await _solutionProvider.TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new FindSymbolResult(null, error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new FindSymbolResult(null,
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "find-symbol")));
            }

            return new FindSymbolResult(symbol.ToSymbolDescriptor());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FindSymbol failed for {SymbolId}", request.SymbolId);
            return new FindSymbolResult(null,
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to resolve symbol '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "find-symbol")));
        }
    }

    public async Task<GetSymbolAtPositionResult> GetSymbolAtPositionAsync(GetSymbolAtPositionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Path) || request.Line <= 0 || request.Column <= 0)
        {
            return new GetSymbolAtPositionResult(null,
                NavigationErrorFactory.CreateError(ErrorCodes.InvalidRequest,
                    "path, line and column must be provided.",
                    ("operation", "get_symbol_at_position")));
        }

        try
        {
            var (solution, error) = await _solutionProvider.TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new GetSymbolAtPositionResult(null, error);
            }

            var symbol = await _symbolLookupService
                .GetSymbolAtPositionAsync(solution, request.Path, request.Line, request.Column, ct)
                .ConfigureAwait(false);

            if (symbol == null)
            {
                return new GetSymbolAtPositionResult(null,
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        "No symbol could be resolved at the specified position.",
                        ("path", request.Path),
                        ("line", request.Line.ToString()),
                        ("column", request.Column.ToString()),
                        ("operation", "get_symbol_at_position")));
            }

            return new GetSymbolAtPositionResult(symbol.ToSymbolDescriptor());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetSymbolAtPosition failed for {Path}:{Line}:{Column}", request.Path, request.Line, request.Column);
            return new GetSymbolAtPositionResult(null,
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to resolve symbol at position: {ex.Message}",
                    ("operation", "get_symbol_at_position")));
        }
    }

    public async Task<SearchSymbolsResult> SearchSymbolsAsync(SearchSymbolsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        try
        {
            var (solution, error) = await _solutionProvider.TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new SearchSymbolsResult(Array.Empty<SymbolDescriptor>(), 0, error);
            }

            var query = request.Query?.Trim();
            if (string.IsNullOrEmpty(query))
            {
                return new SearchSymbolsResult(Array.Empty<SymbolDescriptor>(), 0);
            }

            var limit = Math.Max(request.Limit ?? int.MaxValue, 0);
            var offset = Math.Max(request.Offset ?? 0, 0);
            var (symbols, total) = await _symbolLookupService.SearchSymbolsAsync(solution, query, offset, limit, ct).ConfigureAwait(false);
            return new SearchSymbolsResult(symbols, total);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SearchSymbols failed for {Query}", request.Query);
            return new SearchSymbolsResult(Array.Empty<SymbolDescriptor>(), 0,
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Search failed: {ex.Message}",
                    ("query", request.Query),
                    ("operation", "search-symbols")));
        }
    }

    public async Task<SearchSymbolsScopedResult> SearchSymbolsScopedAsync(SearchSymbolsScopedRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new SearchSymbolsScopedResult(Array.Empty<SymbolDescriptor>(), 0);
        }

        if (!request.Scope.IsValidSearchScope())
        {
            return new SearchSymbolsScopedResult(Array.Empty<SymbolDescriptor>(),
                0,
                NavigationErrorFactory.CreateError(ErrorCodes.InvalidRequest,
                    "scope must be one of: document, project, solution.",
                    ("parameter", "scope"),
                    ("operation", "search_symbols_scoped")));
        }

        if (string.Equals(request.Scope, SymbolSearchScopes.Document, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(request.Path))
        {
            return new SearchSymbolsScopedResult(Array.Empty<SymbolDescriptor>(),
                0,
                NavigationErrorFactory.CreateError(ErrorCodes.InvalidRequest,
                    "path is required when scope is document.",
                    ("parameter", "path"),
                    ("operation", "search_symbols_scoped")));
        }

        try
        {
            var (solution, error) = await _solutionProvider.TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new SearchSymbolsScopedResult(Array.Empty<SymbolDescriptor>(), 0, error);
            }

            if (!solution.PathExistsInScope(request.Scope, request.Path))
            {
                return new SearchSymbolsScopedResult(Array.Empty<SymbolDescriptor>(),
                    0,
                    NavigationErrorFactory.CreateError(ErrorCodes.PathOutOfScope,
                        "The provided path is outside the selected solution scope.",
                        ("path", request.Path),
                        ("operation", "search_symbols_scoped")));
            }

            var limit = Math.Max(request.Limit ?? int.MaxValue, 0);
            var offset = Math.Max(request.Offset ?? 0, 0);
            var (symbols, total) = await _symbolLookupService
                .SearchSymbolsScopedAsync(solution,
                    request.Query,
                    request.Scope,
                    request.Path,
                    request.Kind,
                    request.Accessibility,
                    offset,
                    limit,
                    ct)
                .ConfigureAwait(false);

            return new SearchSymbolsScopedResult(symbols, total);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SearchSymbolsScoped failed for {Query}", request.Query);
            return new SearchSymbolsScopedResult(Array.Empty<SymbolDescriptor>(),
                0,
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Scoped search failed: {ex.Message}",
                    ("operation", "search_symbols_scoped")));
        }
    }

    public async Task<GetSignatureResult> GetSignatureAsync(GetSignatureRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidInputError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "get_signature");
        if (invalidInputError != null)
        {
            return new GetSignatureResult(null, string.Empty, invalidInputError);
        }

        try
        {
            var (solution, error) = await _solutionProvider.TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new GetSignatureResult(null, string.Empty, error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new GetSignatureResult(null,
                    string.Empty,
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "get_signature")));
            }

            var signature = symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            return new GetSignatureResult(symbol.ToSymbolDescriptor(), signature);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetSignature failed for {SymbolId}", request.SymbolId);
            return new GetSignatureResult(null,
                string.Empty,
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to build signature '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "get_signature")));
        }
    }
}
