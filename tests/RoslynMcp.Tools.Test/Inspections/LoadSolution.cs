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
        result.Projects!.Isolated.IsNotNull();
        result.Projects.Boundary.IsNotNull();
        result.Projects.Interior.IsNotNull();

        result.Projects.Isolated!.Select(project => project.Name).Is(
            "FirstSolutionFailureTests",
            "MixedOutcomeTests",
            "PassingOnlyTests",
            "SecondSolutionFailureTests");

        result.Projects.Boundary!.Select(project => project.Name).Is(
            "ProjectApp",
            "ProjectCore");

        var root = result.Projects.Boundary[0];
        AssertPathEndsWith(root.ProjectPath, "ProjectApp", "ProjectApp.csproj");
        root.References.IsNotNull();
        root.References!.Count.Is(2);
        AssertPathEndsWith(root.References[0], "ProjectCore", "ProjectCore.csproj");
        AssertPathEndsWith(root.References[1], "ProjectImpl", "ProjectImpl.csproj");

        var leaf = result.Projects.Boundary[1];
        AssertPathEndsWith(leaf.ProjectPath, "ProjectCore", "ProjectCore.csproj");
        leaf.References.IsNull();
        leaf.ToJson().Contains("\"references\"", StringComparison.Ordinal).IsFalse();

        result.Projects.Interior!.Select(project => project.Name).Is("ProjectImpl");
        AssertPathEndsWith(result.Projects.Interior[0].ProjectPath, "ProjectImpl", "ProjectImpl.csproj");
        result.Projects.Interior[0].References.IsNotNull();
        result.Projects.Interior[0].References!.Count.Is(1);
        AssertPathEndsWith(result.Projects.Interior[0].References![0], "ProjectCore", "ProjectCore.csproj");
    }

    private static void AssertPathEndsWith(string actualPath, params string[] expectedSegments)
        => actualPath.EndsWith(Path.Combine(expectedSegments), StringComparison.OrdinalIgnoreCase).IsTrue();
}
