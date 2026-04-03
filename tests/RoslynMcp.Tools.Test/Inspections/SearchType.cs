using Is.Assertions;
using RoslynMcp.Tools.Inspection.SearchType;

namespace RoslynMcp.Tools.Test.Inspections;

public sealed class SearchType : LoadedSolutionTests<McpTool>
{
    [Fact]
    public async Task ZeroMatches_ReturnsErrorAndEmptyMatches()
    {
        var result = await Sut.Execute(CancellationToken.None, query: "DefinitelyNotATypeName");

        result.Type.IsNull();
        result.Matches.Count.Is(0);
        result.Error?.Message.Is("no type found");
        result.Error?.Details?["query"].Is("DefinitelyNotATypeName");
    }

    [Fact]
    public async Task OneMatch_ReturnsEmbeddedLoadTypeResult()
    {
        var result = await Sut.Execute(CancellationToken.None, query: "Documentation");

        result.Error.IsNull();
        result.Matches.Count.Is(0);
        result.Type.IsNotNull();

        result.Type!.Symbol.IsNotNull();
        result.Type!.Symbol!.DisplayName.Is("Documentation");
    }

    [Fact]
    public async Task MultipleMatches_ReturnsMinimalMatchList()
    {
        // "Load" matches e.g. LoadMemberScenario, BrokenBuildTests, etc.
        var result = await Sut.Execute(CancellationToken.None, query: "Load");

        result.Type.IsNull();
        result.Error.IsNull();
        result.Matches.Count.IsGreaterThan(1);

        // Ensure token-sparing output: full name + project path only.
        var first = result.Matches[0];
        first.FullName.IsNotNull();
        first.ProjectPath.IsNotNull();
    }
}
