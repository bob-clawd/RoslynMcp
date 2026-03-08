using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.ToolTests;

public sealed class FormatDocumentToolTests(FeatureTestsFixture fixture, ITestOutputHelper output)
    : ToolTests<FormatDocumentTool>(fixture, output)
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task ExecuteAsync_WithInvalidPath_ReturnsError(string? path)
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, path ?? string.Empty);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.InvalidInput);
        result.WasFormatted.IsFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistentPath_ReturnsPathOutOfScope()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, "/nonexistent/path/file.cs");

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.PathOutOfScope);
        result.WasFormatted.IsFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithValidDocument_FormatsSuccessfully()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath);

        // Document may or may not need formatting, but operation should succeed
        result.Error.ShouldBeNone();
        result.FilePath.Is(CodeSmellsPath);
    }
}
