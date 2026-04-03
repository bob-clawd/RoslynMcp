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

    [Fact]
    public async Task GenericArity_IsIncludedInIdentityForDedup()
    {
        var result = await Sut.Execute(CancellationToken.None, query: "GenericFoo");

        result.Type.IsNull();
        result.Error.IsNull();
        result.Matches.Count.Is(2);

        result.Matches.Select(m => m.FullName).OrderBy(x => x, StringComparer.Ordinal).ToArray().Is(
            "ProjectCore.Nested.GenericFoo`1",
            "ProjectCore.Nested.GenericFoo`2");
    }

    [Fact]
    public async Task EnumAndDelegate_UniqueMatch_ResolvesCorrectSymbol()
    {
        var enumResult = await Sut.Execute(CancellationToken.None, query: "InnerEnum");
        enumResult.Error.IsNull();
        enumResult.Type.IsNotNull();
        enumResult.Type!.Symbol!.DisplayName.Is("InnerEnum");

        var delegateResult = await Sut.Execute(CancellationToken.None, query: "InnerDelegate");
        delegateResult.Error.IsNull();
        delegateResult.Type.IsNotNull();
        delegateResult.Type!.Symbol!.DisplayName.Is("InnerDelegate");
    }
}
