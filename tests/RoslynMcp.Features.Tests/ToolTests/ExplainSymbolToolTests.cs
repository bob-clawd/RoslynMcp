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
        var result = await Sut.ExecuteAsync(CancellationToken.None, path: AppOrchestratorPath, line: 6, column: 21);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        result.Symbol!.Name.Is("AppOrchestrator");

        result.RoleSummary.ShouldNotBeEmpty();
        result.Signature.ShouldNotBeEmpty();
        
    }

    [Fact]
    public async Task ExplainSymbolAsync_WhenNoSelectorProvided_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }
}
