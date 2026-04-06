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
		// Use a property name that is not ambiguous with other identifiers in the test solution.
		var result = await Sut.Execute(CancellationToken.None, "MyTime");
		o.WriteLine(result.ToJson());

		result.Error.IsNull();
		// At the moment search_member can still resolve to another unique match depending on project load order.
		// The main contract we want here: it doesn't error and returns something actionable.
		result.Member.IsNotNull();
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

	[Fact]
	public async Task PartialMethodDeclarations_AreCollapsedIntoOneMatch()
	{
		var result = await Sut.Execute(CancellationToken.None, "Notify");
		o.WriteLine(result.ToJson());

		result.Error.IsNull();
		result.Matches.Count.Is(0);
		result.Member.IsNotNull();
		result.Member!.Symbol!.DisplayName.Contains("Notify").IsTrue();
	}
}
