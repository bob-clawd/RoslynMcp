using Is.Assertions;
using RoslynMcp.Tools.Inspection.LoadSolution;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Test.Inspections;

[TraceWatch]
public class LoadSolution(ITestOutputHelper o) : Tests<McpTool>
{
	private static IEnumerable<ProjectSummary> EnumerateAllProjects(ProjectOutputBuckets buckets)
	{
		IEnumerable<ProjectSummary> From(ProjectBuckets? b)
		{
			if (b is null)
				return [];
			return (b.Leaves ?? [])
				.Concat(b.Intermediates ?? [])
				.Concat(b.Roots ?? []);
		}

		return From(buckets.Libraries)
			.Concat(From(buckets.Executables))
			.Concat(From(buckets.Unknown));
	}

	private static void AssertCountsMatchEdges(Result result)
	{
		var projects = EnumerateAllProjects(result.Projects).ToList();
		var byPath = projects
			.Where(p => !string.IsNullOrWhiteSpace(p.ProjectPath))
			.ToDictionary(p => p.ProjectPath!, StringComparer.OrdinalIgnoreCase);

		var outgoing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		var incoming = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (var p in byPath.Keys)
		{
			outgoing[p] = 0;
			incoming[p] = 0;
		}

		foreach (var e in result.Edges.References)
		{
			// We intentionally ignore pathless projects; only count edges for projects we emitted.
			if (!outgoing.ContainsKey(e.From) || !incoming.ContainsKey(e.To))
				continue;

			outgoing[e.From]++;
			incoming[e.To]++;
		}

		foreach (var (path, p) in byPath)
		{
			p.References.Is(outgoing[path]);
			p.ReferencedBy.Is(incoming[path]);
		}
	}

	[Fact]
	public async Task HappyPath_WithoutFile()
	{
		var result = await Sut.Execute(CancellationToken.None);
		o.WriteLine(result.ToJson());

		result.Projects.Count.Is(7);
		result.Edges.Count.Is(3);
		result.Edges.CycleDetected.IsFalse();
		result.Edges.References.Count.Is(3);
		AssertCountsMatchEdges(result);
	}

	[Fact]
	public async Task HappyPath_WithFile()
	{
		var result = await Sut.Execute(CancellationToken.None, "TestSolution.sln");
		o.WriteLine(result.ToJson());

		result.Error?.Message.IsEmpty();
		result.Projects.Count.Is(7);
		result.Edges.Count.Is(3);
		result.Edges.CycleDetected.IsFalse();
		result.Edges.References.Count.Is(3);
		AssertCountsMatchEdges(result);
	}
	
	[Fact]
	public async Task HappyPath_WithFolder()
	{
		SetWorkspaceDirectory(Directory.GetParent(TestSolutionDirectory)!.FullName);
	
		var result = await Sut.Execute(CancellationToken.None, "TestSolution");
		o.WriteLine(result.ToJson());

		result.Error?.Message.IsEmpty();
		result.Projects.Count.Is(7);
		result.Edges.Count.Is(3);
		result.Edges.CycleDetected.IsFalse();
		result.Edges.References.Count.Is(3);
		AssertCountsMatchEdges(result);
	}
}
