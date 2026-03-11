using Is.Assertions;
using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using RoslynMcp.Features.Tests.Mutations;
using RoslynMcp.Features.Tools;
using RoslynMcp.Features.Tools.Inspections;
using Xunit;
using Xunit.Abstractions;

namespace RoslynMcp.Features.Tests.Inspections.Tools;

public sealed class TraceCallFlowToolTests(SharedSandboxFixture fixture, ITestOutputHelper output)
    : SharedToolTests<TraceCallFlowTool>(fixture, output)
{
    [Fact]
    public async Task TraceFlowAsync_WithResolvedRunAsyncSymbol_ReturnsStableDownstreamEdges()
    {
        var runAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 15, column: 44);
        var startAsync = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "ProcessingSession.Lifecycle"), line: 5, column: 23);
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 54, column: 35);
        var calculate = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "CodeSmells"), line: 23, column: 16);
        var stop = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "ProcessingSession.Lifecycle"), line: 12, column: 17);
        var changeState = await ResolveSymbolAsync(GetFilePath("ProjectImpl", "ProcessingSession.State"), line: 11, column: 18);

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId: runAsync.Symbol!.SymbolId, direction: "downstream", depth: 2);

        result.Error.ShouldBeNone();
        result.RootSymbol.IsNotNull();
        result.RootSymbol!.Name.Is("RunAsync");
        result.RootSymbol.Kind.Is("Method");
        result.Direction.Is("downstream");
        result.Depth.Is(2);
        result.Edges.Count.Is(9);

        result.Edges.AssertEdge(runAsync.Symbol.SymbolId, startAsync.Symbol!.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 20);
        result.Edges.AssertEdge(runAsync.Symbol.SymbolId, executeFlowAsync.Symbol!.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 23);
        result.Edges.AssertEdge(runAsync.Symbol.SymbolId, calculate.Symbol!.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 25);
        result.Edges.AssertEdge(runAsync.Symbol.SymbolId, stop.Symbol!.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 27);
        result.Edges.AssertEdge(startAsync.Symbol.SymbolId, changeState.Symbol!.SymbolId, Path.Combine("ProjectImpl", "ProcessingSession.Lifecycle.cs"), 7);
        result.Edges.AssertEdge(stop.Symbol.SymbolId, changeState.Symbol.SymbolId, Path.Combine("ProjectImpl", "ProcessingSession.Lifecycle.cs"), 14);

        var directEdge = result.Edges.GetEdge(runAsync.Symbol.SymbolId, startAsync.Symbol.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 20);
        directEdge.FromSymbolId.ShouldBeExternalSymbolId();
        directEdge.ToSymbolId.ShouldBeExternalSymbolId();
        directEdge.EvidenceKind.Is(FlowEvidenceKinds.DirectStatic);
        directEdge.Uncertainties.IsNotNull();
        var directUncertainties = directEdge.Uncertainties!;
        directUncertainties.IsEmpty();
        directEdge.PossibleTargets.IsNotNull();
        var directPossibleTargets = directEdge.PossibleTargets!;
        directPossibleTargets.IsEmpty();
        directEdge.FromReference.IsNotNull();
        directEdge.ToReference.IsNotNull();
        result.PossibleTargetEdges.IsNotNull();
        result.PossibleTargetEdges!.IsEmpty();

        result.Transitions.Any(static transition => transition.FromProject == "unknown" || transition.ToProject == "unknown").IsFalse();
        result.Transitions.Any(static transition => transition is { FromProject: "ProjectApp", ToProject: "ProjectCore" }).IsTrue();
        result.Transitions.Any(static transition => transition is { FromProject: "ProjectApp", ToProject: "ProjectImpl" }).IsTrue();
        result.Transitions.Any(static transition => transition is { FromProject: "ProjectApp", ToProject: "ProjectApp" }).IsTrue();
        result.Transitions.Any(static transition => transition is { FromProject: "ProjectImpl", ToProject: "ProjectImpl" }).IsTrue();
    }

    [Fact]
    public async Task TraceFlowAsync_WithExecuteFlowAsyncSymbol_ReturnsStableUpstreamEdgesAcrossDepths()
    {
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 54, column: 35);
        var runAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 15, column: 44);
        var runFastAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 78, column: 41);
        var runSafeAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 83, column: 41);

        var depthOne = await Sut.ExecuteAsync(CancellationToken.None, symbolId: executeFlowAsync.Symbol!.SymbolId, direction: "upstream", depth: 1);
        var depthTwo = await Sut.ExecuteAsync(CancellationToken.None, symbolId: executeFlowAsync.Symbol.SymbolId, direction: "upstream", depth: 2);

        depthOne.Error.ShouldBeNone();
        depthOne.RootSymbol.IsNotNull();
        depthOne.RootSymbol!.Name.Is("ExecuteFlowAsync");
        depthOne.Direction.Is("upstream");
        depthOne.Depth.Is(1);
        depthOne.Edges.Count.Is(1);
        depthOne.Edges.AssertEdge(runAsync.Symbol!.SymbolId, executeFlowAsync.Symbol.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 23);

        depthTwo.Error.ShouldBeNone();
        depthTwo.Direction.Is("upstream");
        depthTwo.Depth.Is(2);
        depthTwo.Edges.Count.Is(3);
        depthTwo.Edges.AssertEdge(runAsync.Symbol.SymbolId, executeFlowAsync.Symbol.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 23);
        depthTwo.Edges.AssertEdge(runFastAsync.Symbol!.SymbolId, runAsync.Symbol.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 80);
        depthTwo.Edges.AssertEdge(runSafeAsync.Symbol!.SymbolId, runAsync.Symbol.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 85);
        depthTwo.Transitions.Any(static transition => transition.FromProject == "unknown" || transition.ToProject == "unknown").IsFalse();
        depthTwo.Transitions.AssertTransition("ProjectApp", "ProjectApp", 3);
    }

    [Fact]
    public async Task TraceFlowAsync_WithExecuteFlowAsyncSymbolAndBothDirection_ReturnsIncomingAndOutgoingEdges()
    {
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 54, column: 35);
        var runAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 15, column: 44);
        var runFastAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 78, column: 41);
        var runSafeAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 83, column: 41);
        var operationExecuteAsync = await ResolveSymbolAsync(ContractsPath, line: 18, column: 19);

        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId: executeFlowAsync.Symbol!.SymbolId, direction: "both", depth: 2);

        result.Error.ShouldBeNone();
        result.RootSymbol.IsNotNull();
        result.RootSymbol!.Name.Is("ExecuteFlowAsync");
        result.Direction.Is("both");
        result.Depth.Is(2);
        result.Edges.Count.Is(4);

        result.Edges.AssertEdge(runAsync.Symbol!.SymbolId, executeFlowAsync.Symbol.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 23);
        result.Edges.AssertEdge(runFastAsync.Symbol!.SymbolId, runAsync.Symbol.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 80);
        result.Edges.AssertEdge(runSafeAsync.Symbol!.SymbolId, runAsync.Symbol.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 85);
        result.Edges.AssertEdge(executeFlowAsync.Symbol.SymbolId, operationExecuteAsync.Symbol!.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 56);

        var dispatchEdge = result.Edges.GetEdge(executeFlowAsync.Symbol.SymbolId, operationExecuteAsync.Symbol.SymbolId, Path.Combine("ProjectApp", "AppOrchestrator.cs"), 56);
        dispatchEdge.EvidenceKind.Is(FlowEvidenceKinds.DirectStatic);
        dispatchEdge.Uncertainties.IsNotNull();
        var dispatchUncertainties = dispatchEdge.Uncertainties!;
        dispatchUncertainties.Any(static uncertainty => uncertainty.Category == FlowUncertaintyCategories.InterfaceDispatch).IsTrue();
        dispatchEdge.PossibleTargets.IsNotNull();
        var dispatchPossibleTargets = dispatchEdge.PossibleTargets!;
        dispatchPossibleTargets.IsNotEmpty();
        dispatchPossibleTargets.Any(static target => target.Handle == "method:ProjectImpl.FastWorkItemOperation.ExecuteAsync(WorkItem, CancellationToken)").IsTrue();
        dispatchPossibleTargets.Any(static target => target.Handle == "method:ProjectImpl.SafeWorkItemOperation.ExecuteAsync(WorkItem, CancellationToken)").IsTrue();
        result.PossibleTargetEdges.IsNotNull();
        result.PossibleTargetEdges!.IsEmpty();

        result.Transitions.Any(static transition => transition.FromProject == "unknown" || transition.ToProject == "unknown").IsFalse();
        result.Transitions.AssertTransition("ProjectApp", "ProjectApp", 3);
        result.Transitions.AssertTransition("ProjectApp", "ProjectCore", 1);
    }

    [Fact]
    public async Task TraceFlowAsync_WithPathLineAndColumnSelector_ReturnsResolvedRootAndDirectDownstreamEdge()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, path: AppOrchestratorPath, line: 54, column: 35, direction: "downstream", depth: 1);

        result.Error.ShouldBeNone();
        result.RootSymbol.IsNotNull();
        result.RootSymbol!.Name.Is("ExecuteFlowAsync");
        result.RootSymbol.Kind.Is("Method");
        result.RootSymbol.DeclarationLocation.FilePath.ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "AppOrchestrator.cs"));
        result.RootSymbol.DeclarationLocation.Line.Is(54);
        result.Direction.Is("downstream");
        result.Depth.Is(1);
        result.Edges.Count.Is(1);
        result.Edges[0].FromSymbolId.Is(result.RootSymbol.SymbolId);
        result.Edges[0].Location.FilePath.ShouldEndWithPathSuffix(Path.Combine("ProjectApp", "AppOrchestrator.cs"));
        result.Edges[0].Location.Line.Is(56);
    }

    [Fact]
    public async Task TraceFlowAsync_WithReflectionHeavyMethod_FiltersFrameworkOnlyNoiseByDefault()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, path: AppOrchestratorPath, line: 34, column: 41, direction: "downstream", depth: 1);

        result.Error.ShouldBeNone();
        result.RootSymbol.IsNotNull();
        result.RootSymbol!.Name.Is("RunReflectionPathAsync");
        result.Edges.Count.Is(0);
        result.Transitions.Count.Is(0);
        result.Uncertainties.IsNotNull();
        var uncertainties = result.Uncertainties!;
        uncertainties.Any(static uncertainty => uncertainty.Category == FlowUncertaintyCategories.ReflectionBlindspot).IsTrue();
        result.PossibleTargetEdges.IsNotNull();
        result.PossibleTargetEdges!.IsEmpty();
    }

    [Fact]
    public async Task TraceFlowAsync_WithPossibleTargetsMode_ReturnsExplicitPossibleTargetEdges()
    {
        var executeFlowAsync = await ResolveSymbolAsync(AppOrchestratorPath, line: 54, column: 35);
        var result = await Sut.ExecuteAsync(
            CancellationToken.None,
            symbolId: executeFlowAsync.Symbol!.SymbolId,
            direction: "downstream",
            depth: 1,
            includePossibleTargets: true);

        result.Error.ShouldBeNone();
        result.RootSymbol.IsNotNull();
        result.Edges.Count.Is(1);
        result.PossibleTargetEdges.IsNotNull();

        var possibleTargetEdges = result.PossibleTargetEdges!;
        (possibleTargetEdges.Count >= 2).IsTrue();
        possibleTargetEdges.All(static edge => edge.EvidenceKind == FlowEvidenceKinds.PossibleTarget).IsTrue();
        possibleTargetEdges.All(edge => edge.FromSymbolId == executeFlowAsync.Symbol.SymbolId).IsTrue();
        possibleTargetEdges.Any(static edge => edge.ToReference!.Handle == "method:ProjectImpl.FastWorkItemOperation.ExecuteAsync(WorkItem, CancellationToken)").IsTrue();
        possibleTargetEdges.Any(static edge => edge.ToReference!.Handle == "method:ProjectImpl.SafeWorkItemOperation.ExecuteAsync(WorkItem, CancellationToken)").IsTrue();

        var directEdge = result.Edges[0];
        directEdge.EvidenceKind.Is(FlowEvidenceKinds.DirectStatic);
        directEdge.ToReference.IsNotNull();
        directEdge.ToReference!.Handle.Is("method:ProjectCore.IOperation<TInput, TResult>.ExecuteAsync(TInput, CancellationToken)");
        directEdge.Uncertainties.IsNotNull();
        directEdge.Uncertainties!.Any(static uncertainty => uncertainty.Category == FlowUncertaintyCategories.InterfaceDispatch).IsTrue();
    }

    [Fact]
    public async Task TraceFlowAsync_WithGeneratedRootSymbol_FiltersGeneratedEdgesByDefault()
    {
        var generatedPath = Path.Combine(TestSolutionDirectory, "ProjectApp", "obj", "Debug", "net10.0", "GeneratedExecutionHooks.g.cs");
        var result = await Sut.ExecuteAsync(CancellationToken.None, path: generatedPath, line: 8, column: 24, direction: "downstream", depth: 1);

        result.Error.ShouldBeNone();
        result.RootSymbol.IsNotNull();
        result.RootSymbol!.Name.Is("BeforeRun");
        result.Edges.Count.Is(0);
        result.Transitions.Count.Is(0);
    }

    [Fact]
    public async Task TraceFlowAsync_WithInvalidDirection_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId: "symbol-id", direction: "sideways");

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
    }

    [Fact]
    public async Task TraceFlowAsync_WithUnresolvedSymbolId_ReturnsSymbolNotFound()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, symbolId: "ProjectApp:DoesNotExist", direction: "downstream");

        result.Error.ShouldHaveCode(ErrorCodes.SymbolNotFound);
        result.RootSymbol.IsNull();
        result.Edges.IsEmpty();
    }

    [Fact]
    public async Task TraceFlowAsync_WithoutSelector_ReturnsValidationError()
    {
        var result = await Sut.ExecuteAsync(CancellationToken.None, direction: "downstream");

        result.Error.ShouldHaveCode(ErrorCodes.InvalidInput);
        result.RootSymbol.IsNull();
        result.Edges.IsEmpty();
    }

    private async Task<ResolvedSymbolSummaryResult> ResolveSymbolAsync(string path, int line, int column)
    {
        var resolver = Context.GetRequiredService<ResolveSymbolTool>();
        var result = await resolver.ExecuteAsync(CancellationToken.None, path: path, line: line, column: column);

        result.Error.ShouldBeNone();
        result.Symbol.IsNotNull();
        return new ResolvedSymbolSummaryResult(result.Symbol!);
    }

    private sealed record ResolvedSymbolSummaryResult(ResolvedSymbolSummary Symbol);
}

file static class AssertionExtensions
{
    extension(IReadOnlyList<CallEdge> edges)
    {
        internal void AssertEdge(string fromSymbolId, string toSymbolId, string expectedFileSuffix, int expectedLine)
        {
            edges.Any(edge =>
                edge.FromSymbolId == fromSymbolId &&
                edge.ToSymbolId == toSymbolId &&
                edge.Location.FilePath.HasPathSuffix(expectedFileSuffix) &&
                edge.Location.Line == expectedLine).IsTrue();
        }

        internal CallEdge GetEdge(string fromSymbolId, string toSymbolId, string expectedFileSuffix, int expectedLine)
            => edges.Single(edge =>
                edge.FromSymbolId == fromSymbolId &&
                edge.ToSymbolId == toSymbolId &&
                edge.Location.FilePath.HasPathSuffix(expectedFileSuffix) &&
                edge.Location.Line == expectedLine);
    }

    extension(IReadOnlyList<FlowTransition> transitions)
    {
        internal void AssertTransition(string fromProject, string toProject, int expectedCount)
        {
            transitions.Any(transition =>
                transition.FromProject == fromProject &&
                transition.ToProject == toProject &&
                transition.Count == expectedCount).IsTrue();
        }
    }
}

public sealed class TraceCallFlowToolIsolatedTests(ITestOutputHelper output)
    : IsolatedToolTests<TraceCallFlowTool>(output)
{
    [Fact]
    public async Task TraceFlowAsync_ExcludesTestFileCallersFromDefaultResults()
    {
        await using var context = await CreateContextAsync();
        var traceTool = GetSut(context);
        var resolveTool = context.GetRequiredService<ResolveSymbolTool>();
        var loadSolution = context.GetRequiredService<LoadSolutionTool>();
        var testFilePath = Path.Combine(context.TestSolutionDirectory, "ProjectApp", "RunAsyncTests.cs");

        await File.WriteAllTextAsync(testFilePath, """
using ProjectCore;
using ProjectImpl;

namespace ProjectApp;

public static class RunAsyncTests
{
    public static Task<OperationResult> ExecuteAsync(CancellationToken cancellationToken = default)
        => new AppOrchestrator(new FastWorkItemOperation()).RunAsync(cancellationToken);
}
""", CancellationToken.None);

        var load = await loadSolution.ExecuteAsync(CancellationToken.None, context.SolutionPath);

        load.Error.ShouldBeNone();

        var runAsync = await resolveTool.ExecuteAsync(CancellationToken.None, path: Path.Combine(context.TestSolutionDirectory, "ProjectApp", "AppOrchestrator.cs"), line: 15, column: 44);

        runAsync.Error.ShouldBeNone();
        runAsync.Symbol.IsNotNull();

        var result = await traceTool.ExecuteAsync(CancellationToken.None, symbolId: runAsync.Symbol!.SymbolId, direction: "upstream", depth: 1);

        result.Error.ShouldBeNone();
        result.Edges.Count.Is(2);
        result.Edges.Any(edge => edge.Location.FilePath.HasPathSuffix(Path.Combine("ProjectApp", "RunAsyncTests.cs"))).IsFalse();
    }

    [Fact]
    public async Task TraceFlowAsync_WithDynamicDispatchRoot_ReportsDynamicUnresolvedBlindspot()
    {
        await using var context = await CreateContextAsync();
        var traceTool = GetSut(context);
        var resolveTool = context.GetRequiredService<ResolveSymbolTool>();
        var loadSolution = context.GetRequiredService<LoadSolutionTool>();
        var dynamicPath = Path.Combine(context.TestSolutionDirectory, "ProjectApp", "DynamicDispatchProbe.cs");

        await File.WriteAllTextAsync(dynamicPath, """
namespace ProjectApp;

public static class DynamicDispatchProbe
{
    public static string RunDynamic(object value)
    {
        dynamic candidate = value;
        return candidate.ToString();
    }
}
""", CancellationToken.None);

        var load = await loadSolution.ExecuteAsync(CancellationToken.None, context.SolutionPath);

        load.Error.ShouldBeNone();

        var root = await resolveTool.ExecuteAsync(CancellationToken.None, path: dynamicPath, line: 5, column: 26);

        root.Error.ShouldBeNone();
        root.Symbol.IsNotNull();

        var result = await traceTool.ExecuteAsync(CancellationToken.None, symbolId: root.Symbol!.SymbolId, direction: "downstream", depth: 1);

        result.Error.ShouldBeNone();
        result.RootSymbol.IsNotNull();
        result.Uncertainties.IsNotNull();
        var uncertainties = result.Uncertainties!;
        uncertainties.Any(static uncertainty => uncertainty.Category == FlowUncertaintyCategories.DynamicUnresolved).IsTrue();
    }
}
