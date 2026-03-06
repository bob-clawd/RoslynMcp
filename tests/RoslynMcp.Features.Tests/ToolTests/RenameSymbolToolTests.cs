using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.ToolTests;

[Collection("MutatingTests")]
public sealed class RenameSymbolToolTests(MutatingTestsFixture fixture, ITestOutputHelper output)
{
    private readonly MutatingTestsFixture _fixture = fixture;
    private readonly ITestOutputHelper _output = output;

    private RenameSymbolTool Sut => _fixture.GetRequiredService<RenameSymbolTool>();
    private ResolveSymbolTool ResolveTool => _fixture.GetRequiredService<ResolveSymbolTool>();

    [Theory]
    [InlineData("not-a-real-symbol-id", "NewName", ErrorCodes.SymbolNotFound)]
    [InlineData("   ", "NewName", ErrorCodes.InvalidInput)]
    [InlineData("valid-symbol-id", "", ErrorCodes.InvalidInput)]
    [InlineData("valid-symbol-id", "   ", ErrorCodes.InvalidInput)]
    public async Task RenameSymbolAsync_WithInvalidInputs_ReturnsExpectedError(string symbolId, string newName, string expectedErrorCode)
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, newName);

        result.Error.ShouldHaveCode(expectedErrorCode);
        result.RenamedSymbolId.IsNull();
        result.ChangedDocumentCount.Is(0);
        result.ChangedFiles.IsEmpty();
    }

    [Fact]
    public async Task RenameSymbolAsync_WithInvalidNewName_ReturnsValidationError()
    {
        var symbolId = await ResolveOriginalInterfaceSymbolIdAsync();

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, "123Invalid");

        result.Error.IsNotNull();
        result.RenamedSymbolId.IsNull();
        result.ChangedDocumentCount.Is(0);
    }

    [Fact]
    public async Task RenameSymbolAsync_WithValidSymbol_RenamesSuccessfully()
    {
        var symbolId = await ResolveOriginalInterfaceSymbolIdAsync();
        var newName = "IRenamedInterface";

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId, newName);

        result.Error.ShouldBeNone();
        result.RenamedSymbolId.IsNotNull();
        result.ChangedDocumentCount.IsGreaterThan(0);
        result.ChangedFiles.IsNotEmpty();
        result.AffectedLocations.IsNotEmpty();
        
        Trace($"Renamed to: {result.RenamedSymbolId}");
        Trace($"Changed documents: {result.ChangedDocumentCount}");
        Trace($"Changed files: {string.Join(", ", result.ChangedFiles)}");
        
        // Cleanup: rename back to original to not affect other tests
        var renameBackResult = await Sut.ExecuteAsync(CancellationToken.None, result.RenamedSymbolId!, "IOriginalInterface");
        renameBackResult.Error.ShouldBeNone();
    }

    private async Task<string> ResolveOriginalInterfaceSymbolIdAsync()
    {
        // Resolve by file path and position (interface is on line 3, column 18 in TestTypes.cs)
        var sourcePath = Path.Combine(Path.GetDirectoryName(_fixture.SolutionPath)!, "TestProject", "TestTypes.cs");
        var resolved = await ResolveTool.ExecuteAsync(
            CancellationToken.None, 
            sourcePath,
            line: 3,
            column: 18);

        resolved.Error.ShouldBeNone();
        resolved.Symbol.IsNotNull();

        return resolved.Symbol!.SymbolId;
    }

    private void Trace(string message) => _output.WriteLine(nameof(RenameSymbolTool) + ": " + message);
}
