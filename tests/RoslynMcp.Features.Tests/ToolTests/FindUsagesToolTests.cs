using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.ToolTests;

public sealed class FindUsagesToolTests(FeatureTestsFixture fixture, ITestOutputHelper output)
    : ToolTests<FindUsagesTool>(fixture, output)
{
    [Fact]
    public async Task FindUsagesAsync_WithSolutionScope_ReturnsReferences()
    {
        var resolver = Fixture.GetRequiredService<ResolveSymbolTool>();
        var contractsPath = Path.Combine(Path.GetDirectoryName(Fixture.SolutionPath)!, "ProjectCore", "Contracts.cs");
        var resolved = await resolver.ExecuteAsync(CancellationToken.None, path: contractsPath, line: 31, column: 24);

        resolved.Error.ShouldBeNone();

        var result = await Sut.ExecuteAsync(CancellationToken.None, resolved.Symbol!.SymbolId, scope: "solution");

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.TotalCount.IsGreaterThan(0);
        result.References.Any(reference => reference.FilePath.EndsWith("AppOrchestrator.cs", StringComparison.OrdinalIgnoreCase)).Is(true);
    }

    [Fact]
    public async Task FindUsagesAsync_WithInvalidScope_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, "symbol-id", scope: "invalid");

        result.Error.ShouldHaveCode(ErrorCodes.InvalidRequest);
    }
}
