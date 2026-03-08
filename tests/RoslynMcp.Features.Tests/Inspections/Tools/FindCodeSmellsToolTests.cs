using Is.Assertions;
using System.Reflection;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tools;
using RoslynMcp.Infrastructure.Agent;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed record ExpectedCodeSmellFinding(int Line, int Column, string Title, string Category, string RiskLevel);

public sealed class FindCodeSmellsToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<FindCodeSmellsTool>(fixture, output)
{
    [Fact]
    public async Task FindCodeSmellsAsync_WithNoOptionalFilters_PreservesCompatibility()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath);

        result.Error.ShouldBeNone();
        result.Actions.Any(static action => action.RiskLevel == "blocked").IsFalse();
        result.Actions.All(static action => action.RiskLevel is "low" or "review_required" or "high" or "info").IsTrue();
        result.Actions.All(static action => action.Category is "analyzer" or "correctness" or "design" or "maintainability" or "performance" or "style").IsTrue();
        result.Actions.All(static action => action.ReviewKind is "style_suggestion" or "code_fix_hint" or "review_concern").IsTrue();
        result.Actions.GroupBy(static action => (action.Location.Line, action.Title, action.Category, action.RiskLevel)).All(static group => group.Count() == 1).IsTrue();
        result.Warnings.Any(static warning => warning.Contains("Deduplicated", StringComparison.Ordinal)).IsTrue();
        result.Context.SourceBias.Is(SourceBiases.Handwritten);
        result.Context.ResultCompleteness.Is(ResultCompletenessStates.Complete);
        result.Groups.IsNotNull();
        result.Groups!.IsNotEmpty();
        result.Warnings.Any(static warning => warning.Contains("reviewMode=conservative", StringComparison.Ordinal)).IsFalse();
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithMixedQualityFile_PrioritizesReviewConcernsAheadOfStyleSuggestions()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath);

        result.Error.ShouldBeNone();
        result.Actions.Count.IsGreaterThan(0);
        result.Actions.Take(3).Any(static action => action.ReviewKind == CodeSmellReviewKinds.ReviewConcern).IsTrue();

        var firstStyleIndex = result.Actions.Select(static (action, index) => new { action, index })
            .Where(static item => item.action.ReviewKind == CodeSmellReviewKinds.StyleSuggestion)
            .Select(static item => item.index)
            .DefaultIfEmpty(-1)
            .First();
        if (firstStyleIndex >= 0)
        {
            result.Actions.Take(firstStyleIndex).Any(static action => action.ReviewKind == CodeSmellReviewKinds.ReviewConcern).IsTrue();
        }
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithConservativeReviewMode_ReturnsLowerNoiseSubset()
    {
        var defaultResult = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath);
        var conservativeResult = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath, reviewMode: CodeSmellReviewModes.Conservative);

        defaultResult.Error.ShouldBeNone();
        conservativeResult.Error.ShouldBeNone();
        conservativeResult.Actions.Count.IsGreaterThan(0);
        (conservativeResult.Actions.Count < defaultResult.Actions.Count).IsTrue();
        conservativeResult.Actions.All(static action => action.ReviewKind != CodeSmellReviewKinds.StyleSuggestion).IsTrue();
        conservativeResult.Warnings.Any(static warning => warning.Contains("reviewMode=conservative", StringComparison.Ordinal)).IsTrue();
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithConservativeReviewModeAndAnalyzerFilter_RemainsSensible()
    {
        var result = await Sut.ExecuteAsync(
            CancellationToken.None,
            CodeSmellsPath,
            categories: ["analyzer"],
            reviewMode: CodeSmellReviewModes.Conservative);

        result.Error.ShouldBeNone();
        result.Actions.Count.IsGreaterThan(0);
        result.Actions.All(static action => action.Category == "analyzer").IsTrue();
        result.Actions.All(static action => action.ReviewKind == CodeSmellReviewKinds.CodeFixHint).IsTrue();
    }

    [Fact]
    public async Task FindCodeSmellsAsync_ExposesGroupMetadata_ForNearbyRelatedFindings()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath);

        result.Error.ShouldBeNone();
        result.Groups.IsNotNull();
        var groups = result.Groups!;
        groups.All(static group => !string.IsNullOrWhiteSpace(group.GroupId)).IsTrue();
        result.Actions.All(static action => !string.IsNullOrWhiteSpace(action.GroupId)).IsTrue();
        result.Actions.Any(action => groups.Any(group => group.GroupId == action.GroupId)).IsTrue();
        groups.Any(static group => group.MatchCount > 1).IsTrue();
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithMaxFindings_LimitsReturnedResults()
    {
        var limited = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath, maxFindings: 3);

        limited.Error.ShouldBeNone();
        (limited.Actions.Count <= 3).IsTrue();
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithRiskLevels_FiltersAcceptedFindings()
    {
        var filtered = await Sut.ExecuteAsync(
            CancellationToken.None,
            CodeSmellsPath,
            riskLevels: ["high"]);

        filtered.Error.ShouldBeNone();
        filtered.Actions.All(static action => action.RiskLevel == "high").IsTrue();
        filtered.Actions.Count.IsGreaterThan(0);
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithCategories_FiltersAcceptedFindings()
    {
        var filtered = await Sut.ExecuteAsync(
            CancellationToken.None,
            CodeSmellsPath,
            categories: ["analyzer"]);

        filtered.Error.ShouldBeNone();
        ShouldMatchFindings(filtered.Actions, AnalyzerFindings);
        filtered.Actions.All(static action => action.ReviewKind == CodeSmellReviewKinds.CodeFixHint).IsTrue();
    }

    [Fact]
    public void FindCodeSmellsAsync_CollapsesNearbyRepeatedAddBracesSuggestions()
    {
        var warnings = new List<string>();
        var matches = new[]
        {
            new CodeSmellMatch("Add braces", "style", new SourceLocation(CodeSmellsPath, 10, 9), "refactoring", "low", CodeSmellReviewKinds.StyleSuggestion),
            new CodeSmellMatch("Add braces", "style", new SourceLocation(CodeSmellsPath, 11, 9), "refactoring", "low", CodeSmellReviewKinds.StyleSuggestion),
            new CodeSmellMatch("Add braces", "style", new SourceLocation(CodeSmellsPath, 20, 9), "refactoring", "low", CodeSmellReviewKinds.StyleSuggestion)
        };

        var method = typeof(CodeSmellFindingService).GetMethod("DeduplicateMatches", BindingFlags.NonPublic | BindingFlags.Static);

        method.IsNotNull();

        var result = (IReadOnlyList<CodeSmellMatch>)method!
            .Invoke(null, [matches, warnings])!;

        result.Count.Is(2);
        warnings.Any(static warning => warning.Contains("Collapsed", StringComparison.Ordinal)).IsTrue();
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithEmptyPath_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, string.Empty);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithInvalidMaxFindings_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath, maxFindings: 0);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithUnsupportedCategory_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath, categories: ["security"]);

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }

    [Fact]
    public async Task FindCodeSmellsAsync_WithUnsupportedReviewMode_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, CodeSmellsPath, reviewMode: "aggressive");

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }

    private static readonly ExpectedCodeSmellFinding[] AnalyzerFindings =
    [
        new(29, 29, "Diagnostic: RCS1163", "analyzer", "info"),
        new(37, 9, "Diagnostic: CS0162", "analyzer", "info")
    ];

    private static void ShouldMatchFindings(IReadOnlyList<CodeSmellMatch> actual, ExpectedCodeSmellFinding[] expected)
    {
        actual.Count.Is(expected.Length);

        for (var i = 0; i < expected.Length; i++)
        {
            var actualFinding = actual[i];
            var expectedFinding = expected[i];

            actualFinding.Location.Line.Is(expectedFinding.Line);
            actualFinding.Location.Column.Is(expectedFinding.Column);
            actualFinding.Title.Is(expectedFinding.Title);
            actualFinding.Category.Is(expectedFinding.Category);
            actualFinding.RiskLevel.Is(expectedFinding.RiskLevel);
        }
    }
}
