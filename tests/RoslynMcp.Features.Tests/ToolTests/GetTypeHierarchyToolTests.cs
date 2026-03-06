using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.ToolTests;

public sealed class GetTypeHierarchyToolTests(FeatureTestsFixture fixture, ITestOutputHelper output)
    : ToolTests<GetTypeHierarchyTool>(fixture, output)
{
    [Fact]
    public async Task GetTypeHierarchyAsync_WithClassSymbol_ReturnsBaseAndDerivedTypes()
    {
        var resolver = Fixture.GetRequiredService<ResolveSymbolTool>();
        var hierarchyPath = Path.Combine(Path.GetDirectoryName(Fixture.SolutionPath)!, "ProjectCore", "Hierarchy.cs");
        var resolved = await resolver.ExecuteAsync(CancellationToken.None, path: hierarchyPath, line: 23, column: 18);

        resolved.Error.ShouldBeNone();

        var result = await Sut.ExecuteAsync(CancellationToken.None, resolved.Symbol!.SymbolId);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.BaseTypes.Select(static type => type.Name).IsContaining("BaseClass");
        result.DerivedTypes.Select(static type => type.Name).IsContaining("LeafClass");
    }

    [Fact]
    public async Task GetTypeHierarchyAsync_WithEmptySymbolId_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, string.Empty);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }
}
