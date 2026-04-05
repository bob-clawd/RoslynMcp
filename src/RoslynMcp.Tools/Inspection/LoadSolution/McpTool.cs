using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.LoadSolution;

public sealed record Result(
    string? Path,
    ProjectOutputBuckets Projects,
    IReadOnlyList<Edge> Edges,
    ErrorInfo? Error = null)
{
    public static Result AsError(string message, IReadOnlyDictionary<string, string>? details = null)
        => new(null, new ProjectOutputBuckets(null, null, null, 0, 0, false), [], new ErrorInfo(message, details));
}

public sealed record ProjectSummary(
    string Name,
    string? ProjectPath,
    int References,
    int ReferencedBy);

public sealed record ProjectBuckets(
    IReadOnlyList<ProjectSummary>? Leaves,
    IReadOnlyList<ProjectSummary>? Intermediates,
    IReadOnlyList<ProjectSummary>? Roots);

public sealed record ProjectOutputBuckets(
    ProjectBuckets? Libraries,
    ProjectBuckets? Executables,
    ProjectBuckets? Unknown,
    int Count,
    int EdgeCount,
    bool CycleDetected);

public sealed record Edge(
    string From,
    string To);

[McpServerToolType]
public sealed class McpTool(
    WorkspaceManager workspaceManager,
    SolutionManager solutionManager)
    : Tool
{
    [McpServerTool(Name = "load_solution", Title = "Load Solution", ReadOnly = false, Idempotent = false)]
    [Description("Use this tool when you need to start working with a .NET solution and no solution has been loaded yet. This must be the first tool called in a session before any code analysis or navigation tools can be used.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("(optional): Absolute path to a `.sln` file, or to a directory used as the recursive discovery root for `.sln`/`.slnx` files. If omitted, the tool will auto-detect from the current workspace.")]
        string? solutionHintPath = null
    )
    {
        solutionHintPath = workspaceManager.ToAbsolutePath(solutionHintPath);
        
        var solutionPath = solutionHintPath switch
        {
            null => workspaceManager.DiscoverSolutionPaths().FirstOrDefault(),
            _ when File.Exists(solutionHintPath) => solutionHintPath,
            _ when Directory.Exists(solutionHintPath) => solutionHintPath.DiscoverFiles("*.sln", "*.slnx").OrderBy(path => path.Length).FirstOrDefault(),
            _ => null
        };

        if (solutionPath is null)
            return Result.AsError("no solution found");

        var solution = await solutionManager.Load(solutionPath, cancellationToken);

        return new Result(
            workspaceManager.ToRelativePathIfPossible(solutionPath),
            GetProjectBuckets(solution),
            GetEdges(solution));
    }

    private ProjectOutputBuckets GetProjectBuckets(Solution solution)
    {
        var outgoingByPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var incomingByPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in solution.Projects)
        {
            var projectPath = project.FilePath ?? string.Empty;

            outgoingByPath.TryAdd(projectPath, []);
            incomingByPath.TryAdd(projectPath, []);
        }

        foreach (var project in solution.Projects)
        {
            var sourcePath = project.FilePath ?? string.Empty;

            foreach (var reference in project.ProjectReferences)
            {
                var dependency = solution.GetProject(reference.ProjectId);

                if (dependency?.FilePath is null)
                    continue;

                outgoingByPath[sourcePath].Add(dependency.FilePath);
                incomingByPath[dependency.FilePath].Add(sourcePath);
            }
        }

        var summaries = new List<ProjectSummary>();

        foreach (var project in solution.Projects)
        {
            var projectPath = project.FilePath;
            if (string.IsNullOrWhiteSpace(projectPath))
                continue;

            summaries.Add(new ProjectSummary(
                project.Name,
                workspaceManager.ToRelativePathIfPossible(projectPath),
                References: outgoingByPath[projectPath].Count,
                ReferencedBy: incomingByPath[projectPath].Count));
        }

        var edges = GetEdges(solution);
        var nodes = summaries.Select(p => p.ProjectPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToList();

        var topo = TopoSort(
            nodes: nodes,
            edges: edges.Select(e => (e.From, e.To)).ToList());

        // Split into node-type buckets.
        var leavesList = summaries.Where(p => p.References == 0).ToList();
        var rootsList = summaries.Where(p => p.ReferencedBy == 0).ToList();
        // Prefer root over leaf when both apply (single-project solution).
        leavesList.RemoveAll(p => p.ReferencedBy == 0);
        var intermediatesList = summaries.Except(leavesList).Except(rootsList).ToList();

        // Top-level split by output role.
        // - Libraries: OutputType == "Library"
        // - Executables: OutputType == "Exe" or "WinExe"
        // - Unknown: anything else (including null/NetModule)

        IReadOnlyList<ProjectSummary> SortList(IEnumerable<ProjectSummary> list)
            => list
                .OrderBy(p => p.ProjectPath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .ToList();

        // Classify by output role using Roslyn/MSBuild-like OutputKind mapping.
        static string GetOutputRole(Project project)
        {
            var outputType = project.CompilationOptions?.OutputKind switch
            {
                OutputKind.DynamicallyLinkedLibrary => "Library",
                OutputKind.ConsoleApplication => "Exe",
                OutputKind.WindowsApplication => "WinExe",
                _ => null
            };

            return outputType switch
            {
                "Library" => "libraries",
                "Exe" or "WinExe" => "executables",
                _ => "unknown"
            };
        }

        var pathToRole = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in solution.Projects)
        {
            var filePath = project.FilePath;
            if (string.IsNullOrWhiteSpace(filePath))
                continue;
            var rel = workspaceManager.ToRelativePathIfPossible(filePath);
            pathToRole[rel] = GetOutputRole(project);
        }

        var librariesAll = summaries.Where(p => p.ProjectPath is not null && pathToRole.GetValueOrDefault(p.ProjectPath) == "libraries").ToList();
        var executablesAll = summaries.Where(p => p.ProjectPath is not null && pathToRole.GetValueOrDefault(p.ProjectPath) == "executables").ToList();
        var unknownAll = summaries.Where(p => p.ProjectPath is not null && pathToRole.GetValueOrDefault(p.ProjectPath) == "unknown").ToList();

        ProjectBuckets? BucketsOrNull(IReadOnlyList<ProjectSummary> subset)
        {
            if (subset.Count == 0)
                return null;

            var l = SortList(subset.Where(p => p.References == 0 && p.ReferencedBy != 0));
            var r = SortList(subset.Where(p => p.ReferencedBy == 0));
            var i = SortList(subset.Where(p => !(p.References == 0 && p.ReferencedBy != 0) && p.ReferencedBy != 0));

            IReadOnlyList<ProjectSummary>? NullIfEmpty(IReadOnlyList<ProjectSummary> list)
                => list.Count == 0 ? null : list;

            return new ProjectBuckets(
                Leaves: NullIfEmpty(l),
                Intermediates: NullIfEmpty(i),
                Roots: NullIfEmpty(r));
        }

        var libBuckets = BucketsOrNull(librariesAll);
        var exeBuckets = BucketsOrNull(executablesAll);
        var unkBuckets = BucketsOrNull(unknownAll);

        return new ProjectOutputBuckets(
            Libraries: libBuckets,
            Executables: exeBuckets,
            Unknown: unkBuckets,
            Count: summaries.Count,
            EdgeCount: edges.Count,
            CycleDetected: topo.CycleDetected);
    }

    // Note: previous nested group format removed in favor of explicit leaf/intermediate/root buckets.

    private IReadOnlyList<Edge> GetEdges(Solution solution)
    {
        var edges = new List<Edge>();

        foreach (var project in solution.Projects)
        {
            var fromPathAbs = project.FilePath;
            if (string.IsNullOrWhiteSpace(fromPathAbs))
                continue;

            var from = workspaceManager.ToRelativePathIfPossible(fromPathAbs);

            foreach (var reference in project.ProjectReferences)
            {
                var dependency = solution.GetProject(reference.ProjectId);
                var toPathAbs = dependency?.FilePath;
                if (string.IsNullOrWhiteSpace(toPathAbs))
                    continue;

                var to = workspaceManager.ToRelativePathIfPossible(toPathAbs);
                edges.Add(new Edge(from, to));
            }
        }

        return edges
            .OrderBy(e => e.From, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.To, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Summary fields are now embedded in the Projects node (Count/EdgeCount/CycleDetected).

    private static TopoSortResult TopoSort(IReadOnlyList<string> nodes, IReadOnlyList<(string from, string to)> edges)
    {
        // Graph model: from -> to (project -> referenced-project).
        // For a dependency-first order, we sort the reversed edges: dependency -> dependent.

        var dependents = nodes.ToDictionary(n => n, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        var inDegree = nodes.ToDictionary(n => n, _ => 0, StringComparer.OrdinalIgnoreCase);

        foreach (var (from, to) in edges)
        {
            if (!dependents.ContainsKey(from) || !dependents.ContainsKey(to))
                continue;

            // reverse: to -> from
            dependents[to].Add(from);
            inDegree[from]++;
        }

        var ready = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in nodes)
            if (inDegree[n] == 0)
                ready.Add(n);

        var order = new List<string>(nodes.Count);
        while (ready.Count > 0)
        {
            var n = ready.Min!;
            ready.Remove(n);
            order.Add(n);

            foreach (var m in dependents[n].OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                inDegree[m]--;
                if (inDegree[m] == 0)
                    ready.Add(m);
            }
        }

        var cycleDetected = order.Count != nodes.Count;
        if (cycleDetected)
        {
            foreach (var n in nodes.Where(n => !order.Contains(n, StringComparer.OrdinalIgnoreCase))
                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                order.Add(n);
        }

        var orderIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < order.Count; i++)
            orderIndex[order[i]] = i;

        return new TopoSortResult(order, orderIndex, cycleDetected);
    }

    private sealed record TopoSortResult(
        IReadOnlyList<string> Order,
        IReadOnlyDictionary<string, int> OrderIndex,
        bool CycleDetected);
}
