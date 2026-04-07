using Is.Assertions;
using RoslynMcp.Tools.Inspection.DiagnoseFile;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Test.Inspections;

[TraceWatch]
public class DiagnoseFile(ITestOutputHelper o) : Tests<McpTool>
{
	[Fact]
	public async Task HappyPath_NoErrors_ReturnsEmptyList()
	{
		LoadSolution();

		var result = await Sut.Execute(CancellationToken.None, Path.Combine(WorkspaceDirectory, "ProjectApp", "AppOrchestrator.cs"));
		o.WriteLine(result.ToJson());

		result.Error.IsNull();
		result.Errors.Count.Is(0);
	}

	[Fact]
	public async Task ErrorFile_ReturnsAtLeastOneError()
	{
		LoadSolution();

		var result = await Sut.Execute(CancellationToken.None, Path.Combine(WorkspaceDirectory, "ProjectApp", "Broken.cs"));
		o.WriteLine(result.ToJson());

		result.Error.IsNull();
		result.Errors.Count.IsGreaterThan(0);
		result.Errors[0].Id.Is("CS0103");
	}

}
