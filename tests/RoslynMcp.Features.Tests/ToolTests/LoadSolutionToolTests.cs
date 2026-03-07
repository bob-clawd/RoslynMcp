using Is.Assertions;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.ToolTests;

public sealed class LoadSolutionToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<LoadSolutionTool>(fixture, output)
{
    [Fact]
    public void LoadSolutionAsync_WithAbsoluteSolutionPath_LoadsExpectedProjects()
    {
        var result = Context.LoadedSolution;

        result.SelectedSolutionPath.Is(Context.SolutionPath);
        string.Equals(Context.SolutionPath, Context.CanonicalSolutionPath, StringComparison.OrdinalIgnoreCase).IsFalse();
        result.Error.ShouldBeNone();

        var projectNames = result.Projects.Select(static project => project.Name).ToArray();
        
        projectNames.IsContaining("ProjectApp");
        projectNames.IsContaining("ProjectCore");
        projectNames.IsContaining("ProjectImpl");
    }
}
