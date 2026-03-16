using Is.Assertions;
using RoslynMcp.Features.Tools;
using RoslynMcp.Features.Tools.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed class UnderstandProjectsToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<UnderstandProjectsTool>(fixture, output)
{
    [Fact]
    public async Task UnderstandProjectsAsync_WithQuickProfile_ReturnsProjectRelationshipsWithoutTypesOrHotspots()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, profile: "quick");

        result.Error.ShouldBeNone();
        result.Profile.Is("quick");
        result.Hotspots.Count.Is(0);

        var projectsByName = result.Projects.ToDictionary(static project => project.Name, StringComparer.Ordinal);
        projectsByName.Keys.OrderBy(static name => name, StringComparer.Ordinal).Is("ProjectApp", "ProjectCore", "ProjectImpl");

        projectsByName["ProjectApp"].OutgoingDependencyProjectPaths.Is(Context.GetProject("ProjectCore").Path, Context.GetProject("ProjectImpl").Path);
        projectsByName["ProjectApp"].IncomingDependencyProjectPaths.Count.Is(0);
        projectsByName["ProjectApp"].Types.Count.Is(0);

        projectsByName["ProjectCore"].OutgoingDependencyProjectPaths.Count.Is(0);
        projectsByName["ProjectCore"].IncomingDependencyProjectPaths.Is(Context.GetProject("ProjectApp").Path, Context.GetProject("ProjectImpl").Path);
        projectsByName["ProjectCore"].Types.Count.Is(0);

        projectsByName["ProjectImpl"].OutgoingDependencyProjectPaths.Is(Context.GetProject("ProjectCore").Path);
        projectsByName["ProjectImpl"].IncomingDependencyProjectPaths.Is(Context.GetProject("ProjectApp").Path);
        projectsByName["ProjectImpl"].Types.Count.Is(0);
    }

    [Fact]
    public async Task UnderstandProjectsAsync_WithInvalidProfile_FallsBackToStandardAndReturnsCompactTypes()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, profile: "invalid-profile");

        result.Error.ShouldBeNone();
        result.Profile.Is("standard");
        result.Hotspots.Count.Is(0);

        var projectApp = result.Projects.Single(static project => project.Name == "ProjectApp");
        projectApp.Types.Count.Is(3);
        projectApp.Types.All(static type => type.Contains(": ", StringComparison.Ordinal)).IsTrue();
        projectApp.Types.Select(static type => type.Split(": ", 2, StringSplitOptions.None)[0]).ToList().ForEach(static symbolId => symbolId.ShouldBeExternalSymbolId());
        projectApp.Types.Is(
            MatchCompactType("ProjectApp.AppEntryPoints", projectApp.Types),
            MatchCompactType("ProjectApp.AppOrchestrator", projectApp.Types),
            MatchCompactType("ProjectApp.MethodMutationTestTarget", projectApp.Types));
    }

    [Fact]
    public async Task UnderstandProjectsAsync_WithDeepProfile_ReturnsTypesAndTenHotspots()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, profile: "deep");

        result.Error.ShouldBeNone();
        result.Profile.Is("deep");
        result.Projects.All(static project => project.Types.Count > 0).IsTrue();
        result.Hotspots.Count.Is(10);
        result.Hotspots.Select(static hotspot => hotspot.SymbolId).ToList().ForEach(static symbolId => symbolId.ShouldBeExternalSymbolId());
        result.Hotspots.All(static hotspot => !hotspot.Location!.FilePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase) && !hotspot.Location.FilePath.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase)).IsTrue();
    }

    private static string MatchCompactType(string qualifiedTypeName, IReadOnlyList<string> types)
        => types.Single(type => type.EndsWith(qualifiedTypeName, StringComparison.Ordinal));
}
