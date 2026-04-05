using System.ComponentModel;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.LoadSolution;

public sealed record Result(
    string? Path,
    SolutionSummary Summary,
    IReadOnlyList<ProjectSummary> Projects,
    IReadOnlyList<Edge> Edges,
    ErrorInfo? Error = null)
{
    public static Result AsError(string message, IReadOnlyDictionary<string, string>? details = null)
        => new(null, new SolutionSummary(0, 0, false), [], [], new ErrorInfo(message, details));
}

public sealed record SolutionSummary(
    int ProjectCount,
    int EdgeCount,
    bool CycleDetected);

public sealed record ProjectSummary(
    string Name,
    string? ProjectPath,
    string? OutputType,
    int ReferenceCount,
    int ReferencedByCount,
    string? NodeType);

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
            GetSolutionSummary(solution),
            GetProjects(solution),
            GetEdges(solution));
    }
    
    private IReadOnlyList<ProjectSummary> GetProjects(Solution solution)
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
                // Prefer MSBuild's OutputType semantics (Exe/Library) over Roslyn's OutputKind.
                // This aligns with common .csproj terminology and other tools.
                GetMsBuildLikeOutputType(project),
                outgoingByPath[projectPath].Count,
                incomingByPath[projectPath].Count,
                NodeType: null));
        }

        // Leaves-first traversal is agent-friendly: it presents dependencies before dependents.
        // Provide a stable topo order for the full graph (via Edges) and sort projects accordingly.
        var edges = GetEdges(solution);
        var nodes = summaries.Select(p => p.ProjectPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToList();

        _ = TopoSort(
            nodes: nodes,
            edges: edges.Select(e => (e.From, e.To)).ToList());

        // Group for scanability, especially on large solutions.
        // Order: leaf -> intermediate -> root, then Library -> Exe -> WinExe -> other.
        var sorted = summaries
            .OrderBy(p => GetNodeTypeSortKey(p.ReferenceCount, p.ReferencedByCount))
            .ThenBy(p => GetOutputTypeSortKey(p.OutputType))
            .ThenBy(p => p.ProjectPath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();

        return sorted
            .Select(p => p with { NodeType = GetNodeType(p.ReferenceCount, p.ReferencedByCount) })
            .ToList();
    }

    private static string GetNodeType(int referenceCount, int referencedByCount)
    {
        // Prefer root over leaf when both apply (single-project solution).
        if (referencedByCount == 0)
            return "root";
        if (referenceCount == 0)
            return "leaf";
        return "intermediate";
    }

    private static int GetNodeTypeSortKey(int referenceCount, int referencedByCount)
    {
        // leaf -> intermediate -> root
        if (referenceCount == 0 && referencedByCount != 0)
            return 0;
        if (referencedByCount == 0)
            return 2;
        return 1;
    }

    private static int GetOutputTypeSortKey(string? outputType)
        => outputType switch
        {
            "Library" => 0,
            "Exe" => 1,
            "WinExe" => 2,
            _ => 3
        };

    private static string? GetMsBuildLikeOutputType(Project project)
    {
        // Best-effort mapping from Roslyn OutputKind to MSBuild-style OutputType.
        // MSBuild typically reports: Exe | Library | WinExe (rare).
        return project.CompilationOptions?.OutputKind switch
        {
            OutputKind.DynamicallyLinkedLibrary => "Library",
            OutputKind.ConsoleApplication => "Exe",
            OutputKind.WindowsApplication => "WinExe",
            OutputKind.NetModule => "NetModule",
            _ => project.CompilationOptions?.OutputKind.ToString()
        };
    }

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

    private SolutionSummary GetSolutionSummary(Solution solution)
    {
        var projects = solution.Projects
            .Select(p => workspaceManager.ToRelativePathIfPossible(p.FilePath ?? string.Empty))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var edges = GetEdges(solution);
        var topo = TopoSort(projects, edges.Select(e => (e.From, e.To)).ToList());

        return new SolutionSummary(
            ProjectCount: projects.Count,
            EdgeCount: edges.Count,
            CycleDetected: topo.CycleDetected);
    }

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
