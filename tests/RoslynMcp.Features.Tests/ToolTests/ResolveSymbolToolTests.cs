using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.ToolTests;

public sealed class ResolveSymbolToolTests(FeatureTestsFixture fixture, ITestOutputHelper output)
    : ToolTests<ResolveSymbolTool>(fixture, output)
{
    [Fact]
    public async Task ResolveSymbolAsync_WithQualifiedName_ReturnsResolvedSymbol()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, qualifiedName: "ProjectApp.AppOrchestrator", projectName: "ProjectApp");
        
        result.Error.ShouldBeNone();
        result.IsAmbiguous.Is(false);
        result.Symbol.IsNotNull();
        result.Symbol!.DisplayName.IsContaining("AppOrchestrator");
        result.Symbol.SymbolId.ShouldNotBeEmtpy();
    }

    [Fact]
    public async Task ResolveSymbolAsync_WhenNoSelectorProvided_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }
}
