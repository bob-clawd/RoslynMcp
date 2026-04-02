using Is.Assertions;
using Microsoft.Extensions.DependencyInjection;
using RoslynMcp.Tools.Inspection.LoadMember;
using Xunit.Abstractions;

namespace RoslynMcp.Tools.Test.Inspections;

[TraceWatch]
public class LoadMember(ITestOutputHelper o) : LoadedSolutionTests<McpTool>
{
	[Fact]
	public async Task HappyPath_Method()
	{
		var typeSymbolId = await GetTypeSymbolIdAsync("ProjectApp", "AppOrchestrator");
		var memberSymbolId = await GetMemberSymbolIdAsync(typeSymbolId, "RunAsync");

		var result = await Sut.Execute(CancellationToken.None, memberSymbolId);
		o.WriteLine(result.ToJson());
		
		result.Callers.Count.Is(2);
	}
	
	[Fact]
	public async Task HappyPath_Field()
	{
		var typeSymbolId = await GetTypeSymbolIdAsync("ProjectApp", "AppOrchestrator");
		var memberSymbolId = await GetMemberSymbolIdAsync(typeSymbolId, "_session");

		var result = await Sut.Execute(CancellationToken.None, memberSymbolId);
		o.WriteLine(result.ToJson());
	}
	
	[Fact]
	public async Task HappyPath_Property()
	{
		var typeSymbolId = await GetTypeSymbolIdAsync("ProjectApp", "AppOrchestrator");
		var memberSymbolId = await GetMemberSymbolIdAsync(typeSymbolId, "Duration");

		var result = await Sut.Execute(CancellationToken.None, memberSymbolId);
		o.WriteLine(result.ToJson());
	}
	
	[Fact]
	public async Task HappyPath_Generics()
	{
		var typeSymbolId = await GetTypeSymbolIdAsync("ProjectCore", "GenericWorker");
		var memberSymbolId = await GetMemberSymbolIdAsync(typeSymbolId, "Finish");

		var result = await Sut.Execute(CancellationToken.None, memberSymbolId);
		o.WriteLine(result.ToJson());
		
		result.Overrides.Count.Is(2);
	}
	
	[Fact]
	public async Task HappyPath_WithDocumentation()
	{
		var typeSymbolId = await GetTypeSymbolIdAsync("ProjectCore", "Documentation");
		var memberSymbolId = await GetMemberSymbolIdAsync(typeSymbolId, "Add");

		var result = await Sut.Execute(CancellationToken.None, memberSymbolId);
		o.WriteLine(result.ToJson());
	}

	[Fact]
	public async Task HappyPath_CallTree_WithImplementations()
	{
		var typeSymbolId = await GetTypeSymbolIdAsync("ProjectCore", "ILoadMemberScenarioWithBodyOperation");
		var memberSymbolId = await GetMemberSymbolIdAsync(typeSymbolId, "ExecuteAsync");

		var result = await Sut.Execute(CancellationToken.None, memberSymbolId);
		o.WriteLine(result.ToJson());

		result.Callers.Count.Is(2);
		result.Callees.Count.Is(2);
		result.Implementations.Count.Is(2);
	}

	[Fact]
	public async Task HappyPath_CallTree_WithOverrides()
	{
		var typeSymbolId = await GetTypeSymbolIdAsync("ProjectCore", "LoadMemberScenarioOperationBase");
		var memberSymbolId = await GetMemberSymbolIdAsync(typeSymbolId, "ExecuteAsync");

		var result = await Sut.Execute(CancellationToken.None, memberSymbolId);
		o.WriteLine(result.ToJson());

		result.Callers.Count.Is(4);
		result.Callees.Count.Is(3);
		result.Overrides.Count.Is(2);
	}
	
	private async Task<string> GetTypeSymbolIdAsync(string projectName, string displayName)
	{
		var result = await ServiceProvider.GetRequiredService<Inspection.LoadProject.McpTool>()
			.Execute(CancellationToken.None, projectName);

		return result.Types.Single(type => type.Type?.DisplayName == displayName).Type?.SymbolId ?? string.Empty;
	}

	private async Task<string> GetMemberSymbolIdAsync(string typeSymbolId, string displayName)
	{
		var result = await ServiceProvider.GetRequiredService<Inspection.LoadType.McpTool>()
			.Execute(CancellationToken.None, typeSymbolId);

		return result.Members.Single(member => member.DisplayName.Contains(displayName)).SymbolId;
	}
}
