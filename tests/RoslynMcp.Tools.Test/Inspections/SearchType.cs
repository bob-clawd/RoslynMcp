using Is.Assertions;
using Microsoft.Extensions.DependencyInjection;
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
        result.Truncated.IsNull();
        result.Error.IsNull();
        result.Matches.Count.IsGreaterThan(1);

        // Ensure token-sparing output: full name + project path only.
        var first = result.Matches[0];
        first.FullName.IsNotNull();
        first.ProjectPath.IsNotNull();
    }

    [Fact]
    public async Task AmbiguousMatches_ResolveTypeSymbolIdsOnlyForFirstTen()
    {
        var result = await Sut.Execute(CancellationToken.None, query: "SearchTypeOverflow");

        result.Error.IsNull();
        result.Type.IsNull();
        result.Matches.Count.Is(50);
        result.Matches.Count(m => m.TypeSymbolId is not null).Is(10);
        result.Matches.Take(10).All(m => m.TypeSymbolId is not null).IsTrue();
        result.Matches.Skip(10).All(m => m.TypeSymbolId is null).IsTrue();

        var loadType = ServiceProvider.GetRequiredService<global::RoslynMcp.Tools.Inspection.LoadType.McpTool>();
        var resolved = await loadType.Execute(CancellationToken.None, result.Matches[0].TypeSymbolId);

        resolved.Error.IsNull();
        resolved.Symbol.IsNotNull();
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
    public async Task DelegateGenericArity_IsIncludedInIdentityForDedup()
    {
        var result = await Sut.Execute(CancellationToken.None, query: "GenericDelegate");

        result.Type.IsNull();
        result.Error.IsNull();
        result.Matches.Count.Is(2);

        result.Matches.Select(m => m.FullName).OrderBy(x => x, StringComparer.Ordinal).ToArray().Is(
            "ProjectCore.Nested.GenericDelegate`1",
            "ProjectCore.Nested.GenericDelegate`2");
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

    [Fact]
    public async Task BroadMatch_TruncatesResultList()
    {
        var result = await Sut.Execute(CancellationToken.None, query: "SearchTypeOverflow");

        result.Error.IsNull();
        result.Type.IsNull();
        result.Truncated.Is(true);
        result.Matches.Count.Is(50);
    }
}
