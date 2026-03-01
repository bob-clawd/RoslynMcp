using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Navigation;
using RoslynMcp.McpServer.Tools;
using Is.Assertions;

namespace RoslynMcp.McpServer.Tests;

public sealed class AgentIntentToolRoutingTests
{
    [Fact]
    public async Task IntentTools_AreRoutableAndNormalizeInputs()
    {
        var bootstrap = new RecordingWorkspaceBootstrapService();
        var understanding = new RecordingCodeUnderstandingService();
        var flow = new RecordingFlowTraceService();
        var discovery = new RecordingCodeSmellFindingService();

        var workspace = new LoadSolutionTools(bootstrap);
        var understand = new UnderstandCodebaseTools(understanding);
        var explain = new ExplainSymbolTools(understanding);
        var listTypes = new ListTypesTools(understanding);
        var listMembers = new ListMembersTools(understanding);
        var resolve = new ResolveSymbolTools(understanding);
        var trace = new TraceFlowTools(flow);
        var modification = new CodeSmellTools(discovery);
        var listDependencies = new ListDependenciesTools(understanding);
        var findUsages = new FindUsagesTools(understanding.Navigation);

        await workspace.LoadSolutionAsync(CancellationToken.None, "  ./sample.sln  ");
        await understand.UnderstandCodebaseAsync(CancellationToken.None, "  deep  ");
        await explain.ExplainSymbolAsync(CancellationToken.None, "  sym  ", "  a.cs  ", -1, 0);
        await listTypes.ListTypesAsync(CancellationToken.None, "  Sample.csproj  ", "  SampleProject ", " abc ", "  Sample  ", "  CLASS ", "  PUBLIC  ", -4, -7);
        await listMembers.ListMembersAsync(CancellationToken.None, "  type-id ", "  a.cs  ", -1, 0, " METHOD ", " PUBLIC ", " STATIC ", null, -2, -3);
        await resolve.ResolveSymbolAsync(CancellationToken.None, "  sym  ", "  a.cs  ", -1, 0, "  Sample.Service.Call  ", "  Sample.csproj  ", " SampleProject ", " abc ");
        await trace.TraceFlowAsync(CancellationToken.None, "  sym  ", "  a.cs  ", -1, 0, "  BOTH  ", -9);
        await modification.FindCodeSmellsAsync("  app.cs  ", CancellationToken.None);
        await listDependencies.ListDependenciesAsync(CancellationToken.None, "  /repo/app.csproj  ", "  App  ", "  id-1  ", "  BOTH  ");
        await findUsages.FindUsagesAsync(CancellationToken.None, "  sym  ");
        await findUsages.FindUsagesAsync(CancellationToken.None, "  sym  ", "  SOLUTION  ", "  a.cs  ");

        bootstrap.LastRequest?.SolutionHintPath.Is("./sample.sln");
        understanding.LastUnderstandRequest?.Profile.Is("deep");
        understanding.LastExplainRequest?.SymbolId.Is("sym");
        understanding.LastExplainRequest?.Line.Is(1);
        understanding.LastExplainRequest?.Column.Is(1);
        understanding.LastListTypesRequest?.ProjectPath.Is("Sample.csproj");
        understanding.LastListTypesRequest?.ProjectName.Is("SampleProject");
        understanding.LastListTypesRequest?.ProjectId.Is("abc");
        understanding.LastListTypesRequest?.Kind.Is("class");
        understanding.LastListTypesRequest?.Limit.Is(0);
        understanding.LastListTypesRequest?.Offset.Is(0);
        understanding.LastListMembersRequest?.TypeSymbolId.Is("type-id");
        understanding.LastListMembersRequest?.Kind.Is("method");
        understanding.LastListMembersRequest?.Accessibility.Is("public");
        understanding.LastListMembersRequest?.Binding.Is("static");
        understanding.LastListMembersRequest?.Line.Is(1);
        understanding.LastListMembersRequest?.Column.Is(1);
        understanding.LastListMembersRequest?.Limit.Is(0);
        understanding.LastListMembersRequest?.Offset.Is(0);
        understanding.LastResolveSymbolRequest?.SymbolId.Is("sym");
        understanding.LastResolveSymbolRequest?.QualifiedName.Is("Sample.Service.Call");
        flow.LastRequest?.Direction.Is("both");
        flow.LastRequest?.Depth.Is(0);
        discovery.LastRequest?.Path.Is("app.cs");
        understanding.LastListDependenciesRequest?.ProjectPath.Is("/repo/app.csproj");
        understanding.LastListDependenciesRequest?.ProjectName.Is("App");
        understanding.LastListDependenciesRequest?.ProjectId.Is("id-1");
        understanding.LastListDependenciesRequest?.Direction.Is("both");
        understanding.Navigation.LastReferencesRequest?.SymbolId.Is("sym");
        understanding.Navigation.LastReferencesScopedRequest?.Scope.Is("solution");
        understanding.Navigation.LastReferencesScopedRequest?.Path.Is("a.cs");
    }

    private sealed class RecordingWorkspaceBootstrapService : IWorkspaceBootstrapService
    {
        public LoadSolutionRequest? LastRequest { get; private set; }

        public Task<LoadSolutionResult> LoadSolutionAsync(LoadSolutionRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new LoadSolutionResult(null, string.Empty, string.Empty, Array.Empty<ProjectSummary>(), new DiagnosticsSummary(0, 0, 0, 0)));
        }
    }

    private sealed class RecordingCodeUnderstandingService : ICodeUnderstandingService
    {
        public UnderstandCodebaseRequest? LastUnderstandRequest { get; private set; }
        public ExplainSymbolRequest? LastExplainRequest { get; private set; }
        public ListTypesRequest? LastListTypesRequest { get; private set; }
        public ListMembersRequest? LastListMembersRequest { get; private set; }
        public ResolveSymbolRequest? LastResolveSymbolRequest { get; private set; }
        public ListDependenciesRequest? LastListDependenciesRequest { get; private set; }
        public RecordingNavigationService Navigation { get; } = new();

        public Task<UnderstandCodebaseResult> UnderstandCodebaseAsync(UnderstandCodebaseRequest request, CancellationToken ct)
        {
            LastUnderstandRequest = request;
            return Task.FromResult(new UnderstandCodebaseResult("standard", Array.Empty<ModuleSummary>(), Array.Empty<HotspotSummary>()));
        }

        public Task<ExplainSymbolResult> ExplainSymbolAsync(ExplainSymbolRequest request, CancellationToken ct)
        {
            LastExplainRequest = request;
            return Task.FromResult(new ExplainSymbolResult(null, string.Empty, string.Empty, Array.Empty<string>(), Array.Empty<ImpactHint>()));
        }

        public Task<ListTypesResult> ListTypesAsync(ListTypesRequest request, CancellationToken ct)
        {
            LastListTypesRequest = request;
            return Task.FromResult(new ListTypesResult(Array.Empty<TypeListEntry>(), 0));
        }

        public Task<ListMembersResult> ListMembersAsync(ListMembersRequest request, CancellationToken ct)
        {
            LastListMembersRequest = request;
            return Task.FromResult(new ListMembersResult(Array.Empty<MemberListEntry>(), 0, request.IncludeInherited));
        }

        public Task<ResolveSymbolResult> ResolveSymbolAsync(ResolveSymbolRequest request, CancellationToken ct)
        {
            LastResolveSymbolRequest = request;
            return Task.FromResult(new ResolveSymbolResult(null, false, Array.Empty<ResolveSymbolCandidate>()));
        }

        public Task<ListDependenciesResult> ListDependenciesAsync(ListDependenciesRequest request, CancellationToken ct)
        {
            LastListDependenciesRequest = request;
            return Task.FromResult(new ListDependenciesResult(Array.Empty<ProjectDependency>(), 0));
        }
    }

    private sealed class RecordingNavigationService : INavigationService
    {
        public FindReferencesRequest? LastReferencesRequest { get; private set; }
        public FindReferencesScopedRequest? LastReferencesScopedRequest { get; private set; }

        public Task<FindSymbolResult> FindSymbolAsync(FindSymbolRequest request, CancellationToken ct)
            => Task.FromResult(new FindSymbolResult(null));

        public Task<GetSymbolAtPositionResult> GetSymbolAtPositionAsync(GetSymbolAtPositionRequest request, CancellationToken ct)
            => Task.FromResult(new GetSymbolAtPositionResult(null));

        public Task<SearchSymbolsResult> SearchSymbolsAsync(SearchSymbolsRequest request, CancellationToken ct)
            => Task.FromResult(new SearchSymbolsResult(Array.Empty<SymbolDescriptor>(), 0));

        public Task<SearchSymbolsScopedResult> SearchSymbolsScopedAsync(SearchSymbolsScopedRequest request, CancellationToken ct)
            => Task.FromResult(new SearchSymbolsScopedResult(Array.Empty<SymbolDescriptor>(), 0));

        public Task<GetSignatureResult> GetSignatureAsync(GetSignatureRequest request, CancellationToken ct)
            => Task.FromResult(new GetSignatureResult(null, string.Empty));

        public Task<FindReferencesResult> FindReferencesAsync(FindReferencesRequest request, CancellationToken ct)
        {
            LastReferencesRequest = request;
            return Task.FromResult(new FindReferencesResult(null, Array.Empty<SourceLocation>()));
        }

        public Task<FindReferencesScopedResult> FindReferencesScopedAsync(FindReferencesScopedRequest request, CancellationToken ct)
        {
            LastReferencesScopedRequest = request;
            return Task.FromResult(new FindReferencesScopedResult(null, Array.Empty<SourceLocation>(), 0));
        }

        public Task<FindImplementationsResult> FindImplementationsAsync(FindImplementationsRequest request, CancellationToken ct)
            => Task.FromResult(new FindImplementationsResult(null, Array.Empty<SymbolDescriptor>()));

        public Task<GetTypeHierarchyResult> GetTypeHierarchyAsync(GetTypeHierarchyRequest request, CancellationToken ct)
            => Task.FromResult(new GetTypeHierarchyResult(null, Array.Empty<SymbolDescriptor>(), Array.Empty<SymbolDescriptor>(), Array.Empty<SymbolDescriptor>()));

        public Task<GetSymbolOutlineResult> GetSymbolOutlineAsync(GetSymbolOutlineRequest request, CancellationToken ct)
            => Task.FromResult(new GetSymbolOutlineResult(null, Array.Empty<SymbolMemberOutline>(), Array.Empty<string>()));

        public Task<GetCallersResult> GetCallersAsync(GetCallersRequest request, CancellationToken ct)
            => Task.FromResult(new GetCallersResult(null, Array.Empty<CallEdge>()));

        public Task<GetCalleesResult> GetCalleesAsync(GetCalleesRequest request, CancellationToken ct)
            => Task.FromResult(new GetCalleesResult(null, Array.Empty<CallEdge>()));

        public Task<GetCallGraphResult> GetCallGraphAsync(GetCallGraphRequest request, CancellationToken ct)
            => Task.FromResult(new GetCallGraphResult(null, Array.Empty<CallEdge>(), 0, 0));
    }

    private sealed class RecordingFlowTraceService : IFlowTraceService
    {
        public TraceFlowRequest? LastRequest { get; private set; }

        public Task<TraceFlowResult> TraceFlowAsync(TraceFlowRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new TraceFlowResult(null, "both", 1, Array.Empty<RoslynMcp.Core.Models.Navigation.CallEdge>(), Array.Empty<FlowTransition>()));
        }
    }

    private sealed class RecordingCodeSmellFindingService : ICodeSmellFindingService
    {
        public FindCodeSmellsRequest? LastRequest { get; private set; }

        public Task<FindCodeSmellsResult> FindCodeSmellsAsync(FindCodeSmellsRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new FindCodeSmellsResult(Array.Empty<CodeSmellMatch>(), Array.Empty<string>()));
        }
    }
}