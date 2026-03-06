using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.ToolTests;

public sealed class ExplainSymbolToolTests(FeatureTestsFixture fixture, ITestOutputHelper output)
    : ToolTests<ExplainSymbolTool>(fixture, output)
{
    [Fact]
    public async Task ExplainSymbolAsync_WithSourcePosition_ReturnsExplanation()
    {
        var filePath = Path.Combine(Path.GetDirectoryName(Fixture.SolutionPath)!, "ProjectApp", "AppOrchestrator.cs");
        var result = await Sut.ExecuteAsync(CancellationToken.None, path: filePath, line: 6, column: 21);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.Symbol!.Name.Is("AppOrchestrator");

        result.RoleSummary.ShouldNotBeEmtpy();
        result.Signature.ShouldNotBeEmtpy();
        
    }

    [Fact]
    public async Task ExplainSymbolAsync_WhenNoSelectorProvided_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }
}
