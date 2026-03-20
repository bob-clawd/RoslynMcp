using RoslynMcp.Core.Models;

namespace RoslynMcp.Core;

public static class WorkspacePathContractModelExtensions
{
    extension(LoadSolutionRequest request)
    {
        public LoadSolutionRequest WithWorkspaceAbsolutePaths(string workspaceRoot)
            => request with { SolutionHintPath = request.SolutionHintPath?.ToWorkspaceAbsolutePath(workspaceRoot) };
    }

    extension(ListTypesRequest request)
    {
        public ListTypesRequest WithWorkspaceAbsolutePaths(string workspaceRoot)
            => request with { ProjectPath = request.ProjectPath?.ToWorkspaceAbsolutePath(workspaceRoot) };
    }

    extension(ListMembersRequest request)
    {
        public ListMembersRequest WithWorkspaceAbsolutePaths(string workspaceRoot)
            => request with { Path = request.Path?.ToWorkspaceAbsolutePath(workspaceRoot) };
    }

    extension(ResolveSymbolRequest request)
    {
        public ResolveSymbolRequest WithWorkspaceAbsolutePaths(string workspaceRoot)
            => request with
            {
                Path = request.Path?.ToWorkspaceAbsolutePath(workspaceRoot),
                ProjectPath = request.ProjectPath?.ToWorkspaceAbsolutePath(workspaceRoot)
            };
    }

    extension(ResolveSymbolsBatchRequest request)
    {
        public ResolveSymbolsBatchRequest WithWorkspaceAbsolutePaths(string workspaceRoot)
            => request with
            {
                Entries = request.Entries.Select(entry => entry with
                {
                    Path = entry.Path?.ToWorkspaceAbsolutePath(workspaceRoot),
                    ProjectPath = entry.ProjectPath?.ToWorkspaceAbsolutePath(workspaceRoot)
                }).ToArray()
            };
    }

    extension(ExplainSymbolRequest request)
    {
        public ExplainSymbolRequest WithWorkspaceAbsolutePaths(string workspaceRoot)
            => request with { Path = request.Path?.ToWorkspaceAbsolutePath(workspaceRoot) };
    }

    extension(TraceFlowRequest request)
    {
        public TraceFlowRequest WithWorkspaceAbsolutePaths(string workspaceRoot)
            => request with { Path = request.Path?.ToWorkspaceAbsolutePath(workspaceRoot) };
    }

    extension(FindCodeSmellsRequest request)
    {
        public FindCodeSmellsRequest WithWorkspaceAbsolutePaths(string workspaceRoot)
            => request with { Path = request.Path.ToWorkspaceAbsolutePath(workspaceRoot) };
    }

    extension(RunTestsRequest request)
    {
        public RunTestsRequest WithWorkspaceAbsolutePaths(string workspaceRoot)
            => request with { Target = request.Target?.ToWorkspaceAbsolutePath(workspaceRoot) };
    }

    extension(FindReferencesScopedRequest request)
    {
        public FindReferencesScopedRequest WithWorkspaceAbsolutePaths(string workspaceRoot)
            => request with { Path = request.Path?.ToWorkspaceAbsolutePath(workspaceRoot) };
    }

    extension(FormatDocumentRequest request)
    {
        public FormatDocumentRequest WithWorkspaceAbsolutePaths(string workspaceRoot)
            => request with { Path = request.Path.ToWorkspaceAbsolutePath(workspaceRoot) };
    }

    extension(GetRefactoringsAtPositionRequest request)
    {
        public GetRefactoringsAtPositionRequest WithWorkspaceAbsolutePaths(string workspaceRoot)
            => request with { Path = request.Path.ToWorkspaceAbsolutePath(workspaceRoot) };
    }

    extension(SourceLocation? location)
    {
        public SourceLocation? WithWorkspaceRelativePaths(string workspaceRoot)
            => location is null ? null : location with { FilePath = location.FilePath.ToWorkspaceRelativePathIfPossible(workspaceRoot) };
    }

    extension(SymbolReference? symbolReference)
    {
        public SymbolReference? WithWorkspaceRelativePaths(string workspaceRoot)
            => symbolReference is null ? null : symbolReference with
            {
                DeclarationLocation = symbolReference.DeclarationLocation.WithWorkspaceRelativePaths(workspaceRoot)
            };
    }

    extension(ProjectSummary project)
    {
        public ProjectSummary WithWorkspaceRelativePaths(string workspaceRoot)
            => project with { Path = project.Path?.ToWorkspaceRelativePathIfPossible(workspaceRoot) };
    }

    extension(LoadSolutionResult result)
    {
        public LoadSolutionResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                SelectedSolutionPath = result.SelectedSolutionPath?.ToWorkspaceRelativePathIfPossible(workspaceRoot),
                WorkspaceId = result.WorkspaceId.ToWorkspaceRelativePathIfPossible(workspaceRoot),
                Projects = result.Projects.Select(project => project.WithWorkspaceRelativePaths(workspaceRoot)).ToArray(),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(ProjectLandscapeSummary project)
    {
        public ProjectLandscapeSummary WithWorkspaceRelativePaths(string workspaceRoot)
            => project with
            {
                ProjectPath = project.ProjectPath?.ToWorkspaceRelativePathIfPossible(workspaceRoot),
                OutgoingDependencyProjectPaths = project.OutgoingDependencyProjectPaths.ToWorkspaceRelativePathsIfPossible(workspaceRoot),
                IncomingDependencyProjectPaths = project.IncomingDependencyProjectPaths.ToWorkspaceRelativePathsIfPossible(workspaceRoot)
            };
    }

    extension(HotspotSummary hotspot)
    {
        public HotspotSummary WithWorkspaceRelativePaths(string workspaceRoot)
            => hotspot with { Location = hotspot.Location.WithWorkspaceRelativePaths(workspaceRoot) };
    }

    extension(UnderstandProjectsResult result)
    {
        public UnderstandProjectsResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                Projects = result.Projects.Select(project => project.WithWorkspaceRelativePaths(workspaceRoot)).ToArray(),
                Hotspots = result.Hotspots.Select(hotspot => hotspot.WithWorkspaceRelativePaths(workspaceRoot)).ToArray(),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(TypeListEntry type)
    {
        public TypeListEntry WithWorkspaceRelativePaths(string workspaceRoot)
            => type with { Location = type.Location.WithWorkspaceRelativePaths(workspaceRoot) };
    }

    extension(ListTypesResult result)
    {
        public ListTypesResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                Types = result.Types.Select(type => type.WithWorkspaceRelativePaths(workspaceRoot)).ToArray(),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(MemberListEntry member)
    {
        public MemberListEntry WithWorkspaceRelativePaths(string workspaceRoot)
            => member with { Location = member.Location.WithWorkspaceRelativePaths(workspaceRoot) };
    }

    extension(ListMembersResult result)
    {
        public ListMembersResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                Members = result.Members.Select(member => member.WithWorkspaceRelativePaths(workspaceRoot)).ToArray(),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(ResolvedSymbolSummary? symbol)
    {
        public ResolvedSymbolSummary? WithWorkspaceRelativePaths(string workspaceRoot)
            => symbol is null ? null : symbol with { Location = symbol.Location.WithWorkspaceRelativePaths(workspaceRoot) };
    }

    extension(ResolveSymbolCandidate candidate)
    {
        public ResolveSymbolCandidate WithWorkspaceRelativePaths(string workspaceRoot)
            => candidate with { Location = candidate.Location.WithWorkspaceRelativePaths(workspaceRoot) };
    }

    extension(ResolveSymbolResult result)
    {
        public ResolveSymbolResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                Symbol = result.Symbol.WithWorkspaceRelativePaths(workspaceRoot),
                Candidates = result.Candidates.Select(candidate => candidate.WithWorkspaceRelativePaths(workspaceRoot)).ToArray(),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(ResolveSymbolsBatchItemResult item)
    {
        public ResolveSymbolsBatchItemResult WithWorkspaceRelativePaths(string workspaceRoot)
            => item with
            {
                Symbol = item.Symbol.WithWorkspaceRelativePaths(workspaceRoot),
                Candidates = item.Candidates.Select(candidate => candidate.WithWorkspaceRelativePaths(workspaceRoot)).ToArray(),
                Error = item.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(ResolveSymbolsBatchResult result)
    {
        public ResolveSymbolsBatchResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                Results = result.Results.Select(item => item.WithWorkspaceRelativePaths(workspaceRoot)).ToArray(),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(ReferenceFileGroup fileGroup)
    {
        public ReferenceFileGroup WithWorkspaceRelativePaths(string workspaceRoot)
            => fileGroup with { FilePath = fileGroup.FilePath.ToWorkspaceRelativePathIfPossible(workspaceRoot) };
    }

    extension(CompactSymbolSummary? symbol)
    {
        public CompactSymbolSummary? WithWorkspaceRelativePaths(string workspaceRoot)
            => symbol is null ? null : symbol with { Location = symbol.Location.WithWorkspaceRelativePaths(workspaceRoot) };
    }

    extension(UsageSymbolSummary? symbol)
    {
        public UsageSymbolSummary? WithWorkspaceRelativePaths(string workspaceRoot)
            => symbol is null ? null : symbol with { Location = symbol.Location.WithWorkspaceRelativePaths(workspaceRoot) };
    }

    extension(ExplainSymbolResult result)
    {
        public ExplainSymbolResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                Symbol = result.Symbol.WithWorkspaceRelativePaths(workspaceRoot),
                KeyReferences = result.KeyReferences?.Select(reference => reference.WithWorkspaceRelativePaths(workspaceRoot)).ToArray(),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(TraceRootSummary? root)
    {
        public TraceRootSummary? WithWorkspaceRelativePaths(string workspaceRoot)
            => root is null ? null : root with { Location = root.Location.WithWorkspaceRelativePaths(workspaceRoot) };
    }

    extension(TraceSymbolEntry entry)
    {
        public TraceSymbolEntry WithWorkspaceRelativePaths(string workspaceRoot)
            => entry with { Location = entry.Location.WithWorkspaceRelativePaths(workspaceRoot) };
    }

    extension(TraceFlowEdge edge)
    {
        public TraceFlowEdge WithWorkspaceRelativePaths(string workspaceRoot)
            => edge with { Site = edge.Site.WithWorkspaceRelativePaths(workspaceRoot)! };
    }

    extension(TraceFlowResult result)
    {
        public TraceFlowResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                Root = result.Root.WithWorkspaceRelativePaths(workspaceRoot),
                Symbols = result.Symbols?.ToDictionary(pair => pair.Key, pair => pair.Value.WithWorkspaceRelativePaths(workspaceRoot), StringComparer.Ordinal),
                Edges = result.Edges.Select(edge => edge.WithWorkspaceRelativePaths(workspaceRoot)).ToArray(),
                PossibleTargetEdges = result.PossibleTargetEdges?.Select(edge => edge.WithWorkspaceRelativePaths(workspaceRoot)).ToArray(),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(CodeSmellOccurrenceFile occurrenceFile)
    {
        public CodeSmellOccurrenceFile WithWorkspaceRelativePaths(string workspaceRoot)
            => occurrenceFile with { FilePath = occurrenceFile.FilePath.ToWorkspaceRelativePathIfPossible(workspaceRoot) };
    }

    extension(CodeSmellFindingEntry finding)
    {
        public CodeSmellFindingEntry WithWorkspaceRelativePaths(string workspaceRoot)
            => finding with
            {
                OccurrenceFiles = finding.OccurrenceFiles.Select(file => file.WithWorkspaceRelativePaths(workspaceRoot)).ToArray()
            };
    }

    extension(FindCodeSmellsResult result)
    {
        public FindCodeSmellsResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                Findings = result.Findings.Select(finding => finding.WithWorkspaceRelativePaths(workspaceRoot)).ToArray(),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(FindReferencesScopedResult result)
    {
        public FindReferencesScopedResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                Symbol = result.Symbol.WithWorkspaceRelativePaths(workspaceRoot),
                ReferenceFiles = result.ReferenceFiles.Select(file => file.WithWorkspaceRelativePaths(workspaceRoot)).ToArray(),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(FindImplementationsResult result)
    {
        public FindImplementationsResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                Symbol = result.Symbol.WithWorkspaceRelativePaths(workspaceRoot),
                Implementations = result.Implementations.Select(implementation => implementation.WithWorkspaceRelativePaths(workspaceRoot)!).ToArray(),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(GetTypeHierarchyResult result)
    {
        public GetTypeHierarchyResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                Symbol = result.Symbol.WithWorkspaceRelativePaths(workspaceRoot),
                BaseTypes = result.BaseTypes.Select(type => type.WithWorkspaceRelativePaths(workspaceRoot)!).ToArray(),
                ImplementedInterfaces = result.ImplementedInterfaces.Select(type => type.WithWorkspaceRelativePaths(workspaceRoot)!).ToArray(),
                DerivedTypes = result.DerivedTypes.Select(type => type.WithWorkspaceRelativePaths(workspaceRoot)!).ToArray(),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(TestFailureGroup group)
    {
        public TestFailureGroup WithWorkspaceRelativePaths(string workspaceRoot)
            => group with { File = group.File?.ToWorkspaceRelativePathIfPossible(workspaceRoot) };
    }

    extension(BuildDiagnostic diagnostic)
    {
        public BuildDiagnostic WithWorkspaceRelativePaths(string workspaceRoot)
            => diagnostic with { File = diagnostic.File?.ToWorkspaceRelativePathIfPossible(workspaceRoot) };
    }

    extension(RunTestsResult result)
    {
        public RunTestsResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                FailureGroups = result.FailureGroups.Select(group => group.WithWorkspaceRelativePaths(workspaceRoot)).ToArray(),
                BuildDiagnostics = result.BuildDiagnostics?.Select(diagnostic => diagnostic.WithWorkspaceRelativePaths(workspaceRoot)).ToArray(),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(AffectedFileLocations file)
    {
        public AffectedFileLocations WithWorkspaceRelativePaths(string workspaceRoot)
            => file with { FilePath = file.FilePath.ToWorkspaceRelativePathIfPossible(workspaceRoot) };
    }

    extension(MutationDiagnosticInfo diagnostic)
    {
        public MutationDiagnosticInfo WithWorkspaceRelativePaths(string workspaceRoot)
            => diagnostic with { FilePath = diagnostic.FilePath.ToWorkspaceRelativePathIfPossible(workspaceRoot) };
    }

    extension(DiagnosticsDeltaInfo delta)
    {
        public DiagnosticsDeltaInfo WithWorkspaceRelativePaths(string workspaceRoot)
            => delta with
            {
                NewErrors = delta.NewErrors.Select(diagnostic => diagnostic.WithWorkspaceRelativePaths(workspaceRoot)).ToArray(),
                NewWarnings = delta.NewWarnings.Select(diagnostic => diagnostic.WithWorkspaceRelativePaths(workspaceRoot)).ToArray()
            };
    }

    extension(RenameSymbolResult result)
    {
        public RenameSymbolResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                AffectedLocationFiles = result.AffectedLocationFiles.Select(file => file.WithWorkspaceRelativePaths(workspaceRoot)).ToArray(),
                ChangedFiles = result.ChangedFiles.ToWorkspaceRelativePathsIfPossible(workspaceRoot),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(FormatDocumentResult result)
    {
        public FormatDocumentResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                Path = result.Path.ToWorkspaceRelativePathIfPossible(workspaceRoot),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(AddMethodResult result)
    {
        public AddMethodResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                ChangedFiles = result.ChangedFiles.ToWorkspaceRelativePathsIfPossible(workspaceRoot),
                DiagnosticsDelta = result.DiagnosticsDelta.WithWorkspaceRelativePaths(workspaceRoot),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(DeleteMethodResult result)
    {
        public DeleteMethodResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                ChangedFiles = result.ChangedFiles.ToWorkspaceRelativePathsIfPossible(workspaceRoot),
                DiagnosticsDelta = result.DiagnosticsDelta.WithWorkspaceRelativePaths(workspaceRoot),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(ReplaceMethodResult result)
    {
        public ReplaceMethodResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                ChangedFiles = result.ChangedFiles.ToWorkspaceRelativePathsIfPossible(workspaceRoot),
                DiagnosticsDelta = result.DiagnosticsDelta.WithWorkspaceRelativePaths(workspaceRoot),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }

    extension(ReplaceMethodBodyResult result)
    {
        public ReplaceMethodBodyResult WithWorkspaceRelativePaths(string workspaceRoot)
            => result with
            {
                ChangedFiles = result.ChangedFiles.ToWorkspaceRelativePathsIfPossible(workspaceRoot),
                DiagnosticsDelta = result.DiagnosticsDelta.WithWorkspaceRelativePaths(workspaceRoot),
                Error = result.Error.ToWorkspaceRelativePathIfPossible(workspaceRoot)
            };
    }
}
