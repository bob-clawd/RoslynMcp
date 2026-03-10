using RoslynMcp.Core;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Infrastructure.Agent.Handlers;

internal sealed class ResolveSymbolsBatchHandler(ResolveSymbolHandler resolveSymbolHandler)
{
    private const int MaximumEntries = 100;

    private readonly ResolveSymbolHandler _resolveSymbolHandler = resolveSymbolHandler ?? throw new ArgumentNullException(nameof(resolveSymbolHandler));

    public async Task<ResolveSymbolsBatchResult> HandleAsync(ResolveSymbolsBatchRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Entries.Count == 0)
        {
            return CreateValidationError(
                "entries must contain at least one selector.",
                ("field", "entries"));
        }

        if (request.Entries.Count > MaximumEntries)
        {
            return CreateValidationError(
                $"entries cannot contain more than {MaximumEntries} selectors.",
                ("field", "entries"),
                ("provided", request.Entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        var results = new List<ResolveSymbolsBatchItemResult>(request.Entries.Count);

        for (var i = 0; i < request.Entries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var entry = request.Entries[i];
            var resolved = await _resolveSymbolHandler.HandleAsync(
                    new ResolveSymbolRequest(
                        entry.SymbolId,
                        entry.Path,
                        entry.Line,
                        entry.Column,
                        entry.QualifiedName,
                        entry.ProjectPath,
                        entry.ProjectName,
                        entry.ProjectId),
                    ct)
                .ConfigureAwait(false);

            results.Add(new ResolveSymbolsBatchItemResult(
                i,
                entry.Label,
                resolved.Symbol,
                resolved.IsAmbiguous,
                resolved.Candidates,
                resolved.Error));
        }

        return new ResolveSymbolsBatchResult(
            results,
            results.Count,
            results.Count(static item => item.Symbol is not null),
            results.Count(static item => item.IsAmbiguous),
            results.Count(static item => item.Error is not null));
    }

    private static ResolveSymbolsBatchResult CreateValidationError(string message, params (string Key, string? Value)[] details)
        => new([], 0, 0, 0, 0,
            AgentErrorInfo.Create(ErrorCodes.InvalidInput, message, "Call resolve_symbols with 1-100 selector entries.", details));
}