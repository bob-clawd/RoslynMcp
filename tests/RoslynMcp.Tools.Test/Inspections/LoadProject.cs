using Is.Assertions;
using RoslynMcp.Tools.Inspection.LoadProject;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Test.Inspections;

[TraceWatch]
public class LoadProject(ITestOutputHelper o) : LoadedSolutionTests<McpTool>
{
	[Fact]
	public async Task HappyPath_WithProjectFile()
	{
		var result = await Sut.Execute(CancellationToken.None, Path.Combine("ProjectCore", "ProjectCore.csproj"));
		o.WriteLine(result.ToJson());

		result.Types.Count.Is(28);
	}

	[Fact]
	public async Task HappyPath_WithProjectName()
	{
		var result = await Sut.Execute(CancellationToken.None, "ProjectImpl");
		o.WriteLine(result.ToJson());

		result.Types.Count.Is(10);
	}

	[Fact]
	public async Task UnknownProject_ReturnsProjectPathDetails()
	{
		var result = await Sut.Execute(CancellationToken.None, "missing.csproj");
		o.WriteLine(result.ToJson());

		result.Error?.Message.Is("no project found");
		result.Error?.Details?["projectPath"].Is("missing.csproj");
	}
}

[TraceWatch]
public class LoadProjectWithoutSolution(ITestOutputHelper o) : Tests<McpTool>
{
	[Fact]
	public async Task MissingSolution_ReturnsGuidance()
	{
		var result = await Sut.Execute(CancellationToken.None, "ProjectImpl");
		o.WriteLine(result.ToJson());

		result.Error?.Message.Is("load solution first");
	}
}
