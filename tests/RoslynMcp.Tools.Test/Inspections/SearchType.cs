using Is.Assertions;
using RoslynMcp.Tools.Inspection.SearchType;

namespace RoslynMcp.Tools.Test.Inspections;

public sealed class SearchType : LoadedSolutionTests<McpTool>
{
    [Fact]
    public async Task Execute_returns_type_when_unique_match()
    {
        var result = await Sut.Execute(CancellationToken.None, query: "Documentation");

        result.Error.IsNull();
        result.Matches.Count.Is(0);
        result.Type.IsNotNull();

        result.Type!.Symbol.IsNotNull();
        result.Type!.Symbol!.DisplayName.Is("Documentation");
    }
}
