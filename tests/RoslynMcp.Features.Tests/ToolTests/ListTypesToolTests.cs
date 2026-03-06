using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.ToolTests;

public sealed class ListTypesToolTests(FeatureTestsFixture fixture, ITestOutputHelper output)
    : ToolTests<ListTypesTool>(fixture, output)
{
    [Fact]
    public async Task ListTypesAsync_WithProjectSelector_ReturnsExpectedTypes()
    {
        var project = Fixture.GetProject("ProjectApp");
        var result = await Sut.ExecuteAsync(CancellationToken.None, projectName: project.Name);

        result.Error.ShouldBeNone();
        result.TotalCount.Is(2);
        result.Types.Select(static type => type.DisplayName).Is("AppEntryPoints", "AppOrchestrator");
    }

    [Fact]
    public async Task ListTypesAsync_WithNamespacePrefix_FiltersTypes()
    {
        var project = Fixture.GetProject("ProjectImpl");
        var result = await Sut.ExecuteAsync(CancellationToken.None, projectName: project.Name, namespacePrefix: "ProjectImpl.Internal");

        result.Error.ShouldBeNone();
        result.TotalCount.Is(0);
        result.Types.Count.Is(0);
    }

    [Fact]
    public async Task ListTypesAsync_WithLimitAndOffset_PaginatesDeterministically()
    {
        var project = Fixture.GetProject("ProjectImpl");
        var result = await Sut.ExecuteAsync(CancellationToken.None, projectPath: project.Path, limit: 2, offset: 1);

        result.Error.ShouldBeNone();
        result.TotalCount.Is(7);
        result.Types.Select(static type => type.DisplayName).Is("DefaultFactory<T>", "FastWorkItemOperation");
    }

    [Fact]
    public async Task ListTypesAsync_WhenNoProjectSelectorProvided_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }
}
