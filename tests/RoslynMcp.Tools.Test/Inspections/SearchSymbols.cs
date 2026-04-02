using RoslynMcp.Tools.Inspection.SearchSymbols;
using RoslynMcp.Tools.Managers;
using Microsoft.Extensions.DependencyInjection;

namespace RoslynMcp.Tools.Test.Inspections;

public class SearchSymbolsTests : LoadedSolutionTests<McpTool>
{
    [Fact]
    public async Task HappyPath_SearchTypes_ByWildcard()
    {
        var result = await Sut.Execute(CancellationToken.None, pattern: "*Service");

        Assert.Null(result.Error);
        Assert.True(result.TotalCount >= 0);
    }

    [Fact]
    public async Task HappyPath_SearchMembers_ByName()
    {
        var result = await Sut.Execute(CancellationToken.None, pattern: "Get*", searchKind: "members");

        Assert.Null(result.Error);
    }

    [Fact]
    public async Task HappyPath_SearchAll()
    {
        var result = await Sut.Execute(CancellationToken.None, pattern: "*", searchKind: "all");

        Assert.Null(result.Error);
        Assert.True(result.TotalCount >= 0);
    }

    [Fact]
    public async Task Error_SolutionNotLoaded()
    {
        var symbolManager = ServiceProvider.GetRequiredService<SymbolManager>();
        var tool = new McpTool(
            ServiceProvider.GetRequiredService<WorkspaceManager>(),
            new SolutionManager(symbolManager),
            symbolManager);

        var result = await tool.Execute(CancellationToken.None, pattern: "Test");

        Assert.NotNull(result.Error);
        Assert.Equal("load solution first", result.Error!.Message);
    }

    [Fact]
    public async Task Error_EmptyPattern()
    {
        var result = await Sut.Execute(CancellationToken.None, pattern: "");

        Assert.NotNull(result.Error);
        Assert.Equal("pattern is required", result.Error!.Message);
    }
}
