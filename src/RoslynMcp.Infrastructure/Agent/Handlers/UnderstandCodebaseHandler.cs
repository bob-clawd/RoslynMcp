using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Infrastructure.Agent.Handlers;

/// <summary>
/// Provides overview of codebase: lists types, shows structure, returns diagnostics summary.
/// Used for initial project exploration by AI agents.
/// </summary>
internal sealed class UnderstandCodebaseHandler(CodeUnderstandingQueryService queries, IAnalysisService analysisService)
{
    public async Task<UnderstandCodebaseResult> HandleAsync(UnderstandCodebaseRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profile = request.Profile.NormalizeProfile();

        var (solution, error) = await queries.GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before understanding the codebase.",
            null,
            ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new UnderstandCodebaseResult(profile, [], [],
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

        var metricResult = await analysisService.GetCodeMetricsAsync(new GetCodeMetricsRequest(), ct).ConfigureAwait(false);
        var hotspotCount = profile switch
        {
            "quick" => 3,
            "deep" => 10,
            _ => 5
        };

        var hotspots = await queries.BuildHotspotsAsync(solution, metricResult.Metrics, hotspotCount, ct).ConfigureAwait(false);
        return new UnderstandCodebaseResult(
            profile,
            modules,
            hotspots,
            AgentErrorInfo.Normalize(metricResult.Error, "Run understand_codebase again after diagnostics/metrics collection succeeds."));
    }
}