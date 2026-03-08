using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tests.Mutations;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.ToolTests;

public sealed class FormatDocumentToolTests(ITestOutputHelper output)
    : IsolatedToolTests<FormatDocumentTool>(output)
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_WithInvalidPath_ReturnsError(string path)
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var result = await sut.ExecuteAsync(CancellationToken.None, path);

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.InvalidInput);
        result.WasFormatted.IsFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExistentPath_ReturnsPathOutOfScope()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        var result = await sut.ExecuteAsync(CancellationToken.None, "/nonexistent/path/file.cs");

        result.Error.IsNotNull();
        result.Error?.Code.Is(ErrorCodes.PathOutOfScope);
        result.WasFormatted.IsFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithExistingFile_Works()
    {
        await using var context = await CreateContextAsync();
        var sut = GetSut(context);

        // Use an existing file from the test solution
        var testFile = context.GetFilePath("ProjectApp", "AppOrchestrator");

        // Act - file may or may not need formatting, but operation should succeed
        var result = await sut.ExecuteAsync(CancellationToken.None, testFile);

        // Assert
        result.Error.ShouldBeNone();
        result.FilePath.Is(testFile);
        // WasFormatted depends on whether file needed formatting
    }
}
