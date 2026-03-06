using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Infrastructure.Agent.Handlers;

internal sealed class UnderstandCodebaseHandler
{
    private readonly CodeUnderstandingQueryService _queries;
    private readonly IAnalysisService _analysisService;

    public UnderstandCodebaseHandler(CodeUnderstandingQueryService queries, IAnalysisService analysisService)
    {
        _queries = queries;
        _analysisService = analysisService;
    }

    public async Task<UnderstandCodebaseResult> HandleAsync(UnderstandCodebaseRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profile = request.Profile.NormalizeProfile();

        var (solution, error) = await _queries.GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before understanding the codebase.",
            null,
            ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new UnderstandCodebaseResult(
                profile,
                Array.Empty<ModuleSummary>(),
                Array.Empty<HotspotSummary>(),
                AgentErrorInfo.Normalize(error, "Call load_solution first to select a solution before understanding the codebase."));
        }

        var modules = solution.Projects
            .Select(project =>
            {
                var outgoing = project.ProjectReferences.Count();
                var incoming = solution.Projects.Count(otherProject =>
                    otherProject.ProjectReferences.Any(reference => reference.ProjectId == project.Id));
                return new ModuleSummary(project.Name, project.FilePath, outgoing, incoming);
            })
            .OrderByDescending(static m => m.IncomingDependencies + m.OutgoingDependencies)
            .ThenBy(static m => m.Name, StringComparer.Ordinal)
            .ToArray();

        var metricResult = await _analysisService.GetCodeMetricsAsync(new GetCodeMetricsRequest(), ct).ConfigureAwait(false);
        var hotspotCount = profile switch
        {
            "quick" => 3,
            "deep" => 10,
            _ => 5
        };

        var hotspots = await _queries.BuildHotspotsAsync(solution, metricResult.Metrics, hotspotCount, ct).ConfigureAwait(false);
        return new UnderstandCodebaseResult(
            profile,
            modules,
            hotspots,
            AgentErrorInfo.Normalize(metricResult.Error, "Run understand_codebase again after diagnostics/metrics collection succeeds."));
    }
}
