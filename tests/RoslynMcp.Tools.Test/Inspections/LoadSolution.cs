using Is.Assertions;
using RoslynMcp.Tools.Inspection.LoadSolution;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Test.Inspections;

[TraceWatch]
public class LoadSolution(ITestOutputHelper output) : Tests<McpTool>
{
    [Fact]
    public async Task HappyPath_WithoutFile()
    {
        var result = await Sut.Execute(CancellationToken.None);
        output.WriteLine(result.ToJson());

        AssertProjectGraph(result);
    }

    [Fact]
    public async Task HappyPath_WithFile()
    {
        var result = await Sut.Execute(CancellationToken.None, "TestSolution.sln");
        output.WriteLine(result.ToJson());

        AssertProjectGraph(result);
    }

    [Fact]
    public async Task HappyPath_WithFolder()
    {
        SetWorkspaceDirectory(Directory.GetParent(TestSolutionDirectory)!.FullName);

        var result = await Sut.Execute(CancellationToken.None, "TestSolution");
        output.WriteLine(result.ToJson());

        AssertProjectGraph(result);
    }

    private static void AssertProjectGraph(Result result)
    {
        result.Error.IsNull();
        result.Path.IsNotNull();
        result.Path!.EndsWith("TestSolution.sln", StringComparison.OrdinalIgnoreCase).IsTrue();

        result.Projects.IsNotNull();
        var projects = result.Projects!;

        projects.Roots!.Select(project => project.Name).Is("ProjectApp");
        projects.Leaves!.Select(project => project.Name).Is("ProjectCore");
        projects.Interior!.Select(project => project.Name).Is("ProjectImpl");
        projects.Isolated!.Select(project => project.Name).Is(
            "FirstSolutionFailureTests",
            "MixedOutcomeTests",
            "PassingOnlyTests",
            "SecondSolutionFailureTests");

        var root = projects.Roots[0];
        AssertPathEndsWith(root.ProjectPath, "ProjectApp", "ProjectApp.csproj");
        root.References!.Count.Is(2);
        AssertPathEndsWith(root.References[0], "ProjectCore", "ProjectCore.csproj");
        AssertPathEndsWith(root.References[1], "ProjectImpl", "ProjectImpl.csproj");

        var leaf = projects.Leaves[0];
        AssertPathEndsWith(leaf.ProjectPath, "ProjectCore", "ProjectCore.csproj");
        leaf.References.IsNull();
        leaf.ToJson().Contains("\"references\"", StringComparison.Ordinal).IsFalse();

        var interior = projects.Interior[0];
        AssertPathEndsWith(interior.ProjectPath, "ProjectImpl", "ProjectImpl.csproj");
        interior.References!.Count.Is(1);
        AssertPathEndsWith(interior.References[0], "ProjectCore", "ProjectCore.csproj");
    }

    private static void AssertPathEndsWith(string actualPath, params string[] expectedSegments)
        => actualPath.EndsWith(Path.Combine(expectedSegments), StringComparison.OrdinalIgnoreCase).IsTrue();
}
