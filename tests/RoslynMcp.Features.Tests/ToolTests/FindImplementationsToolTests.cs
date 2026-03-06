using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.ToolTests;

public sealed class FindImplementationsToolTests(FeatureTestsFixture fixture, ITestOutputHelper output)
    : ToolTests<FindImplementationsTool>(fixture, output)
{
    [Fact]
    public async Task FindImplementationsAsync_WithInterfaceSymbol_ReturnsImplementations()
    {
        var resolver = Fixture.GetRequiredService<ResolveSymbolTool>();
        var hierarchyPath = Path.Combine(Path.GetDirectoryName(Fixture.SolutionPath)!, "ProjectCore", "Hierarchy.cs");
        var resolved = await resolver.ExecuteAsync(CancellationToken.None, path: hierarchyPath, line: 3, column: 18);

        resolved.Error.ShouldBeNone();

        var result = await Sut.ExecuteAsync(CancellationToken.None, resolved.Symbol!.SymbolId);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.Implementations.Select(static implementation => implementation.Name).IsContaining("WorkerA", "WorkerB", "RoundRobinWorker");
    }

    [Fact]
    public async Task FindImplementationsAsync_WithEmptySymbolId_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, string.Empty);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }
}
