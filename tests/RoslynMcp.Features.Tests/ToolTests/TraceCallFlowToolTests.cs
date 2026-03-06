using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.ToolTests;

public sealed class TraceCallFlowToolTests(FeatureTestsFixture fixture, ITestOutputHelper output)
    : ToolTests<TraceCallFlowTool>(fixture, output)
{
    [Fact]
    public async Task TraceFlowAsync_WithResolvedMethodSymbol_ReturnsDownstreamEdges()
    {
        var resolver = Fixture.GetRequiredService<ResolveSymbolTool>();

        var resolved = await resolver.ExecuteAsync(CancellationToken.None, path: AppOrchestratorPath, line: 15, column: 44);

        resolved.Error.ShouldBeNone();

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId: resolved.Symbol!.SymbolId, direction: "downstream", depth: 2);

        result.Error.ShouldBeNone();
        result.RootSymbol.IsNotNull();
        result.Direction.Is("downstream");
        result.Edges.Count.Is(10);
    }

    [Fact]
    public async Task TraceFlowAsync_WithInvalidDirection_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId: "symbol-id", direction: "sideways");

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }
}
