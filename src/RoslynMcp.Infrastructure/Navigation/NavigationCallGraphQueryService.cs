using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using Microsoft.Extensions.Logging;
using RoslynMcp.Infrastructure.Agent;

namespace RoslynMcp.Infrastructure.Navigation;

internal sealed class NavigationCallGraphQueryService(
    NavigationSolutionProvider solutionProvider,
    ISymbolLookupService symbolLookupService,
    ICallGraphService callGraphService,
    ILogger logger)
{
    private const int MaxCallGraphDepth = 4;

    private readonly NavigationSolutionProvider _solutionProvider = solutionProvider ?? throw new ArgumentNullException(nameof(solutionProvider));
    private readonly ISymbolLookupService _symbolLookupService = symbolLookupService ?? throw new ArgumentNullException(nameof(symbolLookupService));
    private readonly ICallGraphService _callGraphService = callGraphService ?? throw new ArgumentNullException(nameof(callGraphService));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<GetCallersResult> GetCallersAsync(GetCallersRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidInputError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "get-callers");
        if (invalidInputError != null)
        {
            return new GetCallersResult(null, [], invalidInputError);
        }

        try
        {
            var (solution, error) = await _solutionProvider.TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new GetCallersResult(null, [], error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new GetCallersResult(null, [], NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "get-callers")));
            }

            var depth = Math.Max(request.MaxDepth ?? 1, 1);
            var edges = await _callGraphService.GetCallersAsync(symbol, solution, depth, ct).ConfigureAwait(false);
            return new GetCallersResult(symbol.ToSymbolDescriptor(), edges);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCallers failed for {SymbolId}", request.SymbolId);
            return new GetCallersResult(null, [],
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to compute callers '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "get-callers")));
        }
    }

    public async Task<GetCalleesResult> GetCalleesAsync(GetCalleesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidInputError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "get-callees");
        if (invalidInputError != null)
        {
            return new GetCalleesResult(null, [], invalidInputError);
        }

        try
        {
            var (solution, error) = await _solutionProvider.TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new GetCalleesResult(null, [], error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new GetCalleesResult(null, [],
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "get-callees")));
            }

            var depth = Math.Max(request.MaxDepth ?? 1, 1);
            var edges = await _callGraphService.GetCalleesAsync(symbol, solution, depth, ct).ConfigureAwait(false);
            return new GetCalleesResult(symbol.ToSymbolDescriptor(), edges);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCallees failed for {SymbolId}", request.SymbolId);
            return new GetCalleesResult(null, [],
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to compute callees '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "get-callees")));
        }
    }

    public async Task<GetCallGraphResult> GetCallGraphAsync(GetCallGraphRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var invalidSymbolIdError = NavigationErrorFactory.TryCreateInvalidSymbolIdError(request.SymbolId, "get-callgraph");
        if (invalidSymbolIdError != null)
        {
            return new GetCallGraphResult(null, Array.Empty<CallEdge>(), 0, 0, invalidSymbolIdError);
        }

        if (!_callGraphService.IsValidDirection(request.Direction))
        {
            return new GetCallGraphResult(null, [], 0, 0,
                NavigationErrorFactory.CreateError(ErrorCodes.InvalidRequest,
                    "direction must be one of: incoming, outgoing, both.",
                    ("parameter", "direction"),
                    ("operation", "get-callgraph")));
        }

        try
        {
            var (solution, error) = await _solutionProvider.TryGetSolutionAsync(ct).ConfigureAwait(false);
            if (solution == null)
            {
                return new GetCallGraphResult(null, [], 0, 0, error);
            }

            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new GetCallGraphResult(null, [], 0, 0,
                    NavigationErrorFactory.CreateError(ErrorCodes.SymbolNotFound,
                        $"Symbol '{request.SymbolId}' could not be resolved.",
                        ("symbolId", request.SymbolId),
                        ("operation", "get-callgraph")));
            }

            var maxDepth = Math.Clamp(request.MaxDepth ?? 1, 1, MaxCallGraphDepth);
            var orderedEdges = await _callGraphService
                .GetCallGraphAsync(symbol, solution, request.Direction, maxDepth, ct)
                .ConfigureAwait(false);

            var nodes = new HashSet<string>(StringComparer.Ordinal)
            {
                (symbol.OriginalDefinition ?? symbol).CreateId().ToExternal()
            };

            foreach (var edge in orderedEdges)
            {
                nodes.Add(edge.FromSymbolId);
                nodes.Add(edge.ToSymbolId);
            }

            return new GetCallGraphResult(
                symbol.ToSymbolDescriptor(),
                orderedEdges,
                nodes.Count,
                orderedEdges.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetCallGraph failed for {SymbolId}", request.SymbolId);
            return new GetCallGraphResult(null, [], 0, 0,
                NavigationErrorFactory.CreateError(ErrorCodes.InternalError,
                    $"Failed to build call graph '{request.SymbolId}': {ex.Message}",
                    ("symbolId", request.SymbolId),
                    ("operation", "get-callgraph")));
        }
    }
}
