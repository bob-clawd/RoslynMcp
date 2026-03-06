using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Features.Tools;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.ToolTests;

public sealed class FindCodeSmellsToolTests(FeatureTestsFixture fixture, ITestOutputHelper output)
    : ToolTests<FindCodeSmellsTool>(fixture, output)
{
    [Fact]
    public async Task FindCodeSmellsAsync_WithSolutionFilePath_ReturnsSmellActions()
    {
        var path = Path.Combine(Path.GetDirectoryName(Fixture.SolutionPath)!, "ProjectImpl", "CodeSmells.cs");
        var result = await Sut.ExecuteAsync(CancellationToken.None, path);

        foreach (var item in result.Actions)
            Trace($"{item.Location.Line}|{item.Location.Column}: {item.Title}");
        
        result.Error.ShouldBeNone();
        result.Actions.Count.Is(21);
        
        var titles = result.Actions.Select(action => action.Title).ToArray();

        titles.Any(title => title.Contains("RCS1163")).IsTrue();
        titles.Any(title => title.Contains("Parenthesize")).IsTrue();
        titles.Any(title => title.Contains("Remove")).IsTrue();
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithEmptyPath_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, string.Empty);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }
}
