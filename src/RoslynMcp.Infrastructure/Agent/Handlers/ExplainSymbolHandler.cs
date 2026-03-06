using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Infrastructure.Agent.Handlers;

internal sealed class ExplainSymbolHandler
{
    private readonly CodeUnderstandingQueryService _queries;
    private readonly INavigationService _navigationService;

    public ExplainSymbolHandler(CodeUnderstandingQueryService queries, INavigationService navigationService)
    {
        _queries = queries;
        _navigationService = navigationService;
    }

    public async Task<ExplainSymbolResult> HandleAsync(ExplainSymbolRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (_, bootstrapError) = await _queries.GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before explaining symbols.",
            request.Path,
            ct).ConfigureAwait(false);
        if (bootstrapError != null)
        {
            return new ExplainSymbolResult(
                null,
                string.Empty,
                string.Empty,
                Array.Empty<string>(),
                Array.Empty<ImpactHint>(),
                bootstrapError);
        }

        var symbolResult = await _queries.ResolveSymbolAtRequestAsync(request.SymbolId, request.Path, request.Line, request.Column, ct).ConfigureAwait(false);
        if (symbolResult.Symbol == null)
        {
            return new ExplainSymbolResult(
                null,
                string.Empty,
                string.Empty,
                Array.Empty<string>(),
                Array.Empty<ImpactHint>(),
                AgentErrorInfo.Normalize(symbolResult.Error, "Call explain_symbol with symbolId or path+line+column for an existing symbol."));
        }

        var signature = await _navigationService.GetSignatureAsync(new GetSignatureRequest(symbolResult.Symbol.SymbolId), ct).ConfigureAwait(false);
        var outline = await _navigationService.GetSymbolOutlineAsync(new GetSymbolOutlineRequest(symbolResult.Symbol.SymbolId, 1), ct).ConfigureAwait(false);
        var references = await _navigationService.FindReferencesAsync(new FindReferencesRequest(symbolResult.Symbol.SymbolId), ct).ConfigureAwait(false);

        var keyReferences = references.References
            .Take(5)
            .Select(static r => $"{r.FilePath}:{r.Line}:{r.Column}")
            .ToArray();

        var impactHints = references.References
            .GroupBy(static r => Path.GetFileName(r.FilePath), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select(group => new ImpactHint(group.Key ?? string.Empty, "high reference density", group.Count()))
            .ToArray();

        var roleSummary = outline.Members.Count == 0
            ? $"{symbolResult.Symbol.Kind} '{symbolResult.Symbol.Name}'."
            : $"{symbolResult.Symbol.Kind} '{symbolResult.Symbol.Name}' with {outline.Members.Count} top-level members.";

        return new ExplainSymbolResult(
            symbolResult.Symbol,
            roleSummary,
            signature.Signature,
            keyReferences,
            impactHints,
            AgentErrorInfo.Normalize(signature.Error ?? outline.Error ?? references.Error,
                "Retry explain_symbol for a resolvable symbol in the loaded solution."));
    }
}
