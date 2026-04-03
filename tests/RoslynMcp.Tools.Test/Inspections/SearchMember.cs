using Is.Assertions;
using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Inspection.SearchMember;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Test.Inspections;

[TraceWatch]
public class SearchMember(ITestOutputHelper o) : LoadedSolutionTests<McpTool>
{
	[Fact]
	public async Task UniqueMatch_ReturnsEmbeddedLoadMemberResult()
	{
		var result = await Sut.Execute(CancellationToken.None, "RunAsync");
		o.WriteLine(result.ToJson());

		result.Error.IsNull();
		result.Matches.Count.Is(0);
		result.Member.IsNotNull();
		result.Member!.Symbol!.DisplayName.Contains("RunAsync").IsTrue();
	}

	[Fact]
	public async Task AmbiguousMatch_ReturnsMatchesOnly()
	{
		var result = await Sut.Execute(CancellationToken.None, "ExecuteAsync");
		o.WriteLine(result.ToJson());

		result.Error.IsNull();
		result.Member.IsNull();
		result.Matches.Count.IsGreaterThan(1);
	}

	[Fact]
	public async Task PropertyMatch_IsReturnedInMatches()
	{
		// AppOrchestrator.Duration (property)
		var result = await Sut.Execute(CancellationToken.None, "Duration");
		o.WriteLine(result.ToJson());

		result.Error.IsNull();
		// Unique match returns embedded load_member result.
		result.Member.IsNotNull();
		result.Member!.Symbol!.Kind.Is("property");
	}

	[Fact]
	public async Task MissingQuery_ReturnsError()
	{
		var result = await Sut.Execute(CancellationToken.None, " ");
		o.WriteLine(result.ToJson());

		result.Error.IsNotNull();
	}

	[Fact]
	public async Task Overloads_AreNotCollapsedIntoUniqueResult()
	{
		var result = await Sut.Execute(CancellationToken.None, "Overload");
		o.WriteLine(result.ToJson());

		result.Error.IsNull();
		result.Member.IsNull();
		(result.Matches.Count >= 2).IsTrue();
	}
}
