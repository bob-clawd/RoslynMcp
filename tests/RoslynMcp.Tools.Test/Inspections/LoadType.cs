using Is.Assertions;
using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Inspection.LoadType;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Test.Inspections;

[TraceWatch]
public class LoadType(ITestOutputHelper o) : LoadedSolutionTests<McpTool>
{
	[Fact]
	public async Task HappyPath()
	{
		var id = await GetTypeSymbolIdAsync("ProjectApp", "AppOrchestrator");

		var result = await Sut.Execute(CancellationToken.None, id);
		o.WriteLine(result.ToJson());

		result.Members.Count.Is(15);
	}
	
	[Fact]
	public async Task HappyPath_Interface()
	{
		var symbolId = await GetTypeSymbolIdAsync("ProjectCore", "IOperation");

		var result = await Sut.Execute(CancellationToken.None, symbolId);
		o.WriteLine(result.ToJson());
		
		result.Derived.Count.Is(2);
		result.Implementations.Count.Is(3);
		result.Members.Count.Is(1);
	}
	
	[Fact]
	public async Task Includes_BaseTypes()
	{
		var symbolId = await GetTypeSymbolIdAsync("ProjectImpl", "FastWorkItemOperation");

		var result = await Sut.Execute(CancellationToken.None, symbolId);
		o.WriteLine(result.ToJson());

		result.BaseTypes.Select(type => type.DisplayName).Is("OperationBase", "Object");
	}
	
	[Fact]
	public async Task Includes_Interfaces()
	{
		var symbolId = await GetTypeSymbolIdAsync("ProjectImpl", "FastWorkItemOperation");

		var result = await Sut.Execute(CancellationToken.None, symbolId);
		o.WriteLine(result.ToJson());

		result.Interfaces.Select(type => type.DisplayName).Is("IWorkItemOperation");
		result.Interfaces[0].Location.Is($"ProjectCore{Path.DirectorySeparatorChar}Contracts.cs:31");
	}
	
	[Fact]
	public async Task HappyPath_Generics()
	{
		var symbolId = await GetTypeSymbolIdAsync("ProjectCore", "GenericWorker");

		var result = await Sut.Execute(CancellationToken.None, symbolId);
		o.WriteLine(result.ToJson());
		
		result.Derived.Count.Is(2);
		result.Implementations.Count.Is(0);
		result.Members.Count.Is(3);
	}
	
	[Fact]
	public async Task HappyPath_WithDocumentation()
	{
		var symbolId = await GetTypeSymbolIdAsync("ProjectCore", "Documentation");

		var result = await Sut.Execute(CancellationToken.None, symbolId);
		o.WriteLine(result.ToJson());
		
		result.Derived.Count.Is(0);
		result.Implementations.Count.Is(0);
		result.Members.Count.Is(10);
	}
	
	private async Task<string> GetTypeSymbolIdAsync(string projectName, string displayName)
	{
		var result = await ServiceProvider.GetRequiredService<Inspection.LoadProject.McpTool>()
			.Execute(CancellationToken.None, projectName);

		return result.Types?.Single(type => type.Type?.DisplayName == displayName)?.Type?.SymbolId ?? string.Empty;
	}
}
