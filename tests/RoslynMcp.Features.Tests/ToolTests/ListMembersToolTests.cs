using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.ToolTests;

public sealed class ListMembersToolTests(FeatureTestsFixture fixture, ITestOutputHelper output)
    : ToolTests<ListMembersTool>(fixture, output)
{
    [Fact]
    public async Task ListMembersAsync_WithTypeSymbolId_ReturnsTypeMembers()
    {
        var listTypes = Fixture.GetRequiredService<ListTypesTool>();
        
        var typeResult = await listTypes.ExecuteAsync(CancellationToken.None, projectName: "ProjectApp");
        
        typeResult.Error.ShouldBeNone();

        var appOrchestrator = typeResult.Types.Single(type => type.DisplayName == "AppOrchestrator");

        var result = await Sut.ExecuteAsync(CancellationToken.None, typeSymbolId: appOrchestrator.SymbolId, kind: "method");

        result.Error.ShouldBeNone();
        result.IncludeInherited.Is(false);
        result.TotalCount.IsGreaterThan(0);
        result.Members.Select(static member => member.DisplayName).IsContaining("RunAsync", "RunReflectionPathAsync");
    }

    [Fact]
    public async Task ListMembersAsync_WithInvalidKind_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, typeSymbolId: "ProjectApp|type|AppOrchestrator", kind: "invalid");

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }
}
