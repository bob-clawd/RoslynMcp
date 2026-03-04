using RoslynMcp.Core;
using RoslynMcp.Core.Models.Agent;
using Microsoft.CodeAnalysis;
using RoslynMcp.Infrastructure.Navigation;

namespace RoslynMcp.Infrastructure.Agent.Handlers;

internal sealed class FindUnusedSymbolsHandler
{
    private readonly CodeUnderstandingQueryService _queries;

    public FindUnusedSymbolsHandler(CodeUnderstandingQueryService queries)
    {
        _queries = queries;
    }

    public async Task<FindUnusedSymbolsResult> HandleAsync(FindUnusedSymbolsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (solution, solutionError) = await _queries.GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before finding unused symbols.",
            request.ProjectPath,
            ct).ConfigureAwait(false);

        if (solution == null)
        {
            return new FindUnusedSymbolsResult(
                Array.Empty<UnusedSymbolEntry>(),
                0,
                0,
                Array.Empty<string>(),
                AgentErrorInfo.Normalize(solutionError, "Call load_solution first to select a solution before finding unused symbols."));
        }

        return await _queries.FindUnusedSymbolsAsync(request, solution, ct).ConfigureAwait(false);
    }
}
