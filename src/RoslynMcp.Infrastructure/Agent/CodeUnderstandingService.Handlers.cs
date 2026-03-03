using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using RoslynMcp.Core.Models.Analysis;
using RoslynMcp.Core.Models.Navigation;
using RoslynMcp.Infrastructure.Navigation;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Infrastructure.Agent;

internal sealed class UnderstandCodebaseHandler
{
    private readonly CodeUnderstandingQueryService _queries;
    private readonly IAnalysisService _analysisService;

    public UnderstandCodebaseHandler(CodeUnderstandingQueryService queries, IAnalysisService analysisService)
    {
        _queries = queries;
        _analysisService = analysisService;
    }

    public async Task<UnderstandCodebaseResult> HandleAsync(UnderstandCodebaseRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var profile = CodeUnderstandingQueryService.NormalizeProfile(request.Profile);

        var (solution, error) = await _queries.GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before understanding the codebase.",
            null,
            ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new UnderstandCodebaseResult(
                profile,
                Array.Empty<ModuleSummary>(),
                Array.Empty<HotspotSummary>(),
                AgentErrorInfo.Normalize(error, "Call load_solution first to select a solution before understanding the codebase."));
        }

        var modules = solution.Projects
            .Select(project =>
            {
                var outgoing = project.ProjectReferences.Count();
                var incoming = solution.Projects.Count(otherProject =>
                    otherProject.ProjectReferences.Any(reference => reference.ProjectId == project.Id));
                return new ModuleSummary(project.Name, project.FilePath, outgoing, incoming);
            })
            .OrderByDescending(static m => m.IncomingDependencies + m.OutgoingDependencies)
            .ThenBy(static m => m.Name, StringComparer.Ordinal)
            .ToArray();

        var metricResult = await _analysisService.GetCodeMetricsAsync(new GetCodeMetricsRequest(), ct).ConfigureAwait(false);
        var hotspotCount = profile switch
        {
            "quick" => 3,
            "deep" => 10,
            _ => 5
        };

        var hotspots = await _queries.BuildHotspotsAsync(solution, metricResult.Metrics, hotspotCount, ct).ConfigureAwait(false);
        return new UnderstandCodebaseResult(
            profile,
            modules,
            hotspots,
            AgentErrorInfo.Normalize(metricResult.Error, "Run understand_codebase again after diagnostics/metrics collection succeeds."));
    }
}

internal sealed class ExplainSymbolHandler
{
    private readonly CodeUnderstandingQueryService _queries;
    private readonly INavigationService _navigationService;

    public ExplainSymbolHandler(CodeUnderstandingQueryService queries, INavigationService navigationService)
    {
        _queries = queries;
        _navigationService = navigationService;
    }

    public async Task<ExplainSymbolResult> HandleAsync(ExplainSymbolRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (_, bootstrapError) = await _queries.GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before explaining symbols.",
            request.Path,
            ct).ConfigureAwait(false);
        if (bootstrapError != null)
        {
            return new ExplainSymbolResult(
                null,
                string.Empty,
                string.Empty,
                Array.Empty<string>(),
                Array.Empty<ImpactHint>(),
                bootstrapError);
        }

        var symbolResult = await _queries.ResolveSymbolAtRequestAsync(request.SymbolId, request.Path, request.Line, request.Column, ct).ConfigureAwait(false);
        if (symbolResult.Symbol == null)
        {
            return new ExplainSymbolResult(
                null,
                string.Empty,
                string.Empty,
                Array.Empty<string>(),
                Array.Empty<ImpactHint>(),
                AgentErrorInfo.Normalize(symbolResult.Error, "Call explain_symbol with symbolId or path+line+column for an existing symbol."));
        }

        var signature = await _navigationService.GetSignatureAsync(new GetSignatureRequest(symbolResult.Symbol.SymbolId), ct).ConfigureAwait(false);
        var outline = await _navigationService.GetSymbolOutlineAsync(new GetSymbolOutlineRequest(symbolResult.Symbol.SymbolId, 1), ct).ConfigureAwait(false);
        var references = await _navigationService.FindReferencesAsync(new FindReferencesRequest(symbolResult.Symbol.SymbolId), ct).ConfigureAwait(false);

        var keyReferences = references.References
            .Take(5)
            .Select(static r => $"{r.FilePath}:{r.Line}:{r.Column}")
            .ToArray();

        var impactHints = references.References
            .GroupBy(static r => Path.GetFileName(r.FilePath), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .Select(group => new ImpactHint(group.Key ?? string.Empty, "high reference density", group.Count()))
            .ToArray();

        var roleSummary = outline.Members.Count == 0
            ? $"{symbolResult.Symbol.Kind} '{symbolResult.Symbol.Name}'."
            : $"{symbolResult.Symbol.Kind} '{symbolResult.Symbol.Name}' with {outline.Members.Count} top-level members.";

        return new ExplainSymbolResult(
            symbolResult.Symbol,
            roleSummary,
            signature.Signature,
            keyReferences,
            impactHints,
            AgentErrorInfo.Normalize(signature.Error ?? outline.Error ?? references.Error,
                "Retry explain_symbol for a resolvable symbol in the loaded solution."));
    }
}

internal sealed class ListTypesHandler
{
    private readonly CodeUnderstandingQueryService _queries;

    public ListTypesHandler(CodeUnderstandingQueryService queries)
    {
        _queries = queries;
    }

    public async Task<ListTypesResult> HandleAsync(ListTypesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (solution, solutionError) = await _queries.GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before listing types.",
            request.ProjectPath,
            ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new ListTypesResult(
                Array.Empty<TypeListEntry>(),
                0,
                AgentErrorInfo.Normalize(solutionError, "Call load_solution first to select a solution before listing types."));
        }

        if (!CodeUnderstandingQueryService.TryNormalizeTypeKind(request.Kind, out var normalizedKind))
        {
            return new ListTypesResult(
                Array.Empty<TypeListEntry>(),
                0,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "kind must be one of: class, record, interface, enum, struct.",
                    "Retry list_types with a supported kind filter or omit kind.",
                    ("field", "kind"),
                    ("provided", request.Kind ?? string.Empty),
                    ("expected", "class|record|interface|enum|struct")));
        }

        if (!CodeUnderstandingQueryService.TryNormalizeAccessibility(request.Accessibility, out var normalizedAccessibility))
        {
            return new ListTypesResult(
                Array.Empty<TypeListEntry>(),
                0,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "accessibility must be one of: public, internal, protected, private, protected_internal, private_protected.",
                    "Retry list_types with a supported accessibility filter or omit accessibility.",
                    ("field", "accessibility"),
                    ("provided", request.Accessibility ?? string.Empty)));
        }

        var selectedProjects = CodeUnderstandingQueryService.ResolveProjectSelector(
            solution,
            request.ProjectPath,
            request.ProjectName,
            request.ProjectId,
            selectorRequired: true,
            toolName: "list_types",
            out var selectorError);

        if (selectorError != null)
        {
            return new ListTypesResult(Array.Empty<TypeListEntry>(), 0, selectorError);
        }

        var namespacePrefix = CodeUnderstandingQueryService.NormalizeOptional(request.NamespacePrefix);
        var entries = new List<TypeListEntry>();

        foreach (var project in selectedProjects)
        {
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation == null)
            {
                continue;
            }

            foreach (var type in CodeUnderstandingQueryService.EnumerateTypes(compilation.Assembly.GlobalNamespace))
            {
                if (!type.Locations.Any(static location => location.IsInSource))
                {
                    continue;
                }

                var kind = CodeUnderstandingQueryService.ToTypeKind(type);
                if (kind == null)
                {
                    continue;
                }

                if (normalizedKind != null && !string.Equals(kind, normalizedKind, StringComparison.Ordinal))
                {
                    continue;
                }

                var accessibility = CodeUnderstandingQueryService.NormalizeAccessibility(type.DeclaredAccessibility);
                if (normalizedAccessibility != null && !string.Equals(accessibility, normalizedAccessibility, StringComparison.Ordinal))
                {
                    continue;
                }

                var typeNamespace = CodeUnderstandingQueryService.NormalizeNamespace(type.ContainingNamespace);
                if (namespacePrefix != null && !typeNamespace.StartsWith(namespacePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var (filePath, line, column) = CodeUnderstandingQueryService.GetDeclarationPosition(type);
                entries.Add(new TypeListEntry(
                    type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    SymbolIdentity.CreateId(type),
                    filePath,
                    line,
                    column,
                    kind,
                    CodeUnderstandingQueryService.IsPartial(type),
                    type.Arity > 0 ? type.Arity : null));
            }
        }

        var ordered = entries
            .OrderBy(static item => item.Kind, StringComparer.Ordinal)
            .ThenBy(static item => item.DisplayName, StringComparer.Ordinal)
            .ThenBy(static item => item.Arity ?? 0)
            .ThenBy(static item => item.SymbolId, StringComparer.Ordinal)
            .ToArray();

        var (offset, limit) = CodeUnderstandingQueryService.NormalizePaging(request.Offset, request.Limit);
        var paged = ordered.Skip(offset).Take(limit).ToArray();
        return new ListTypesResult(paged, ordered.Length);
    }
}

internal sealed class ListMembersHandler
{
    private readonly CodeUnderstandingQueryService _queries;

    public ListMembersHandler(CodeUnderstandingQueryService queries)
    {
        _queries = queries;
    }

    public async Task<ListMembersResult> HandleAsync(ListMembersRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (solution, solutionError) = await _queries.GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before listing members.",
            request.Path,
            ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new ListMembersResult(
                Array.Empty<MemberListEntry>(),
                0,
                request.IncludeInherited,
                AgentErrorInfo.Normalize(solutionError, "Call load_solution first to select a solution before listing members."));
        }

        if (!CodeUnderstandingQueryService.TryNormalizeMemberKind(request.Kind, out var normalizedMemberKind))
        {
            return new ListMembersResult(
                Array.Empty<MemberListEntry>(),
                0,
                request.IncludeInherited,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "kind must be one of: method, property, field, event, ctor.",
                    "Retry list_members with a supported kind filter or omit kind.",
                    ("field", "kind"),
                    ("provided", request.Kind ?? string.Empty),
                    ("expected", "method|property|field|event|ctor")));
        }

        if (!CodeUnderstandingQueryService.TryNormalizeAccessibility(request.Accessibility, out var normalizedAccessibility))
        {
            return new ListMembersResult(
                Array.Empty<MemberListEntry>(),
                0,
                request.IncludeInherited,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "accessibility must be one of: public, internal, protected, private, protected_internal, private_protected.",
                    "Retry list_members with a supported accessibility filter or omit accessibility.",
                    ("field", "accessibility"),
                    ("provided", request.Accessibility ?? string.Empty)));
        }

        if (!CodeUnderstandingQueryService.TryNormalizeBinding(request.Binding, out var normalizedBinding))
        {
            return new ListMembersResult(
                Array.Empty<MemberListEntry>(),
                0,
                request.IncludeInherited,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "binding must be one of: static, instance.",
                    "Retry list_members with binding=static or binding=instance, or omit binding.",
                    ("field", "binding"),
                    ("provided", request.Binding ?? string.Empty),
                    ("expected", "static|instance")));
        }

        var typeSymbol = await _queries.ResolveTypeSymbolAsync(request, solution, ct).ConfigureAwait(false);
        if (typeSymbol.Error != null)
        {
            return new ListMembersResult(Array.Empty<MemberListEntry>(), 0, request.IncludeInherited, typeSymbol.Error);
        }

        var symbols = request.IncludeInherited
            ? CodeUnderstandingQueryService.CollectMembersWithInheritance(typeSymbol.Symbol!)
            : typeSymbol.Symbol!.GetMembers();

        var entries = symbols
            .Select(member => CodeUnderstandingQueryService.ToMemberEntry(member, normalizedMemberKind, normalizedAccessibility, normalizedBinding))
            .Where(static entry => entry != null)
            .Select(static entry => entry!)
            .OrderBy(static item => item.Kind, StringComparer.Ordinal)
            .ThenBy(static item => item.DisplayName, StringComparer.Ordinal)
            .ThenBy(static item => item.Signature, StringComparer.Ordinal)
            .ThenBy(static item => item.SymbolId, StringComparer.Ordinal)
            .ToArray();

        var (offset, limit) = CodeUnderstandingQueryService.NormalizePaging(request.Offset, request.Limit);
        var paged = entries.Skip(offset).Take(limit).ToArray();
        return new ListMembersResult(paged, entries.Length, request.IncludeInherited);
    }
}

internal sealed class ResolveSymbolHandler
{
    private readonly CodeUnderstandingQueryService _queries;
    private readonly ISymbolLookupService _symbolLookupService;

    public ResolveSymbolHandler(CodeUnderstandingQueryService queries, ISymbolLookupService symbolLookupService)
    {
        _queries = queries;
        _symbolLookupService = symbolLookupService;
    }

    public async Task<ResolveSymbolResult> HandleAsync(ResolveSymbolRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (solution, solutionError) = await _queries.GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to select a solution before resolving symbols.",
            request.Path ?? request.ProjectPath,
            ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new ResolveSymbolResult(
                null,
                false,
                Array.Empty<ResolveSymbolCandidate>(),
                AgentErrorInfo.Normalize(solutionError, "Call load_solution first to select a solution before resolving symbols."));
        }

        if (!string.IsNullOrWhiteSpace(request.SymbolId))
        {
            var symbol = await _symbolLookupService.ResolveSymbolAsync(request.SymbolId!, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return new ResolveSymbolResult(
                    null,
                    false,
                    Array.Empty<ResolveSymbolCandidate>(),
                    AgentErrorInfo.Create(
                        ErrorCodes.SymbolNotFound,
                        $"symbolId '{request.SymbolId}' could not be resolved.",
                        "Call list_types/list_members or explain_symbol first to obtain a valid symbolId.",
                        ("field", "symbolId"),
                        ("provided", request.SymbolId)));
            }

            return new ResolveSymbolResult(CodeUnderstandingQueryService.ToResolvedSymbol(symbol), false, Array.Empty<ResolveSymbolCandidate>());
        }

        if (!string.IsNullOrWhiteSpace(request.Path) && request.Line.HasValue && request.Column.HasValue)
        {
            var symbol = await _symbolLookupService.GetSymbolAtPositionAsync(
                solution,
                request.Path!,
                request.Line.Value,
                request.Column.Value,
                ct).ConfigureAwait(false);

            if (symbol == null)
            {
                return new ResolveSymbolResult(
                    null,
                    false,
                    Array.Empty<ResolveSymbolCandidate>(),
                    AgentErrorInfo.Create(
                        ErrorCodes.SymbolNotFound,
                        "No symbol found at the provided source position.",
                        "Call resolve_symbol with a valid path+line+column or use list_types/list_members to select a symbolId.",
                        ("field", "path"),
                        ("provided", request.Path)));
            }

            return new ResolveSymbolResult(CodeUnderstandingQueryService.ToResolvedSymbol(symbol), false, Array.Empty<ResolveSymbolCandidate>());
        }

        if (!string.IsNullOrWhiteSpace(request.QualifiedName))
        {
            var selectedProjects = CodeUnderstandingQueryService.ResolveProjectSelector(
                solution,
                request.ProjectPath,
                request.ProjectName,
                request.ProjectId,
                selectorRequired: false,
                toolName: "resolve_symbol",
                out var selectorError);

            if (selectorError != null)
            {
                return new ResolveSymbolResult(null, false, Array.Empty<ResolveSymbolCandidate>(), selectorError);
            }

            var candidates = await CodeUnderstandingQueryService.ResolveByQualifiedNameAsync(request.QualifiedName!, selectedProjects, ct).ConfigureAwait(false);
            if (candidates.Length == 0)
            {
                return new ResolveSymbolResult(
                    null,
                    false,
                    Array.Empty<ResolveSymbolCandidate>(),
                    AgentErrorInfo.Create(
                        ErrorCodes.SymbolNotFound,
                        $"qualifiedName '{request.QualifiedName}' did not match any symbol.",
                        "Refine qualifiedName or provide projectName/projectPath/projectId to narrow the lookup.",
                        ("field", "qualifiedName"),
                        ("provided", request.QualifiedName)));
            }

            if (candidates.Length > 1)
            {
                return new ResolveSymbolResult(
                    null,
                    true,
                    candidates,
                    AgentErrorInfo.Create(
                        ErrorCodes.AmbiguousSymbol,
                        $"qualifiedName '{request.QualifiedName}' matched multiple symbols.",
                        "Select one candidate symbolId and call resolve_symbol again, or scope by projectName/projectPath/projectId.",
                        ("field", "qualifiedName"),
                        ("provided", request.QualifiedName),
                        ("candidateCount", candidates.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            }

            var selected = candidates[0];
            return new ResolveSymbolResult(
                new ResolvedSymbolSummary(selected.SymbolId, selected.DisplayName, selected.Kind, selected.FilePath, selected.Line, selected.Column),
                false,
                Array.Empty<ResolveSymbolCandidate>());
        }

        return new ResolveSymbolResult(
            null,
            false,
            Array.Empty<ResolveSymbolCandidate>(),
            AgentErrorInfo.Create(
                ErrorCodes.InvalidInput,
                "Provide symbolId, path+line+column, or qualifiedName.",
                "Call resolve_symbol with one selector mode: symbolId, source position, or qualifiedName."));
    }
}

internal sealed class ListDependenciesHandler
{
    private readonly CodeUnderstandingQueryService _queries;

    public ListDependenciesHandler(CodeUnderstandingQueryService queries)
    {
        _queries = queries;
    }

    public async Task<ListDependenciesResult> HandleAsync(ListDependenciesRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (solution, solutionError) = await _queries.GetCurrentSolutionWithAutoBootstrapAsync(
            "Call load_solution first to list dependencies.",
            request.ProjectPath,
            ct).ConfigureAwait(false);
        if (solution == null)
        {
            return new ListDependenciesResult(
                Array.Empty<ProjectDependency>(),
                0,
                AgentErrorInfo.Normalize(solutionError, "Call load_solution first to list dependencies."),
                Array.Empty<ProjectDependencyEdge>());
        }

        if (!CodeUnderstandingQueryService.TryNormalizeDependencyDirection(request.Direction, out var direction))
        {
            return new ListDependenciesResult(
                Array.Empty<ProjectDependency>(),
                0,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    $"direction '{request.Direction}' is not valid.",
                    "Use 'outgoing', 'incoming', or 'both'.",
                    ("field", "direction"),
                    ("provided", request.Direction ?? string.Empty)),
                Array.Empty<ProjectDependencyEdge>());
        }

        var hasProjectPath = !string.IsNullOrWhiteSpace(request.ProjectPath);
        var hasProjectName = !string.IsNullOrWhiteSpace(request.ProjectName);
        var hasProjectId = !string.IsNullOrWhiteSpace(request.ProjectId);
        var selectorCount = (hasProjectPath ? 1 : 0) + (hasProjectName ? 1 : 0) + (hasProjectId ? 1 : 0);
        var selectorProvided = selectorCount == 1;

        if (selectorCount > 1)
        {
            return new ListDependenciesResult(
                Array.Empty<ProjectDependency>(),
                0,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "Multiple project selectors provided. Provide exactly one of projectPath, projectName, or projectId.",
                    "Specify only one selector to identify the target project.",
                    ("selectors", $"projectPath:{hasProjectPath}, projectName:{hasProjectName}, projectId:{hasProjectId}")),
                Array.Empty<ProjectDependencyEdge>());
        }

        var normalizedProjectName = CodeUnderstandingQueryService.NormalizeOptional(request.ProjectName);
        if (normalizedProjectName != null)
        {
            var matchingByName = solution.Projects
                .Where(p => string.Equals(p.Name, normalizedProjectName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matchingByName.Length > 1)
            {
                return new ListDependenciesResult(
                    Array.Empty<ProjectDependency>(),
                    0,
                    AgentErrorInfo.Create(
                        ErrorCodes.AmbiguousSymbol,
                        $"projectName '{request.ProjectName}' matched {matchingByName.Length} projects.",
                        "Use projectPath or projectId to disambiguate.",
                        ("field", "projectName"),
                        ("provided", normalizedProjectName),
                        ("matchingCount", matchingByName.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                    Array.Empty<ProjectDependencyEdge>());
            }
        }

        var selectedProjects = CodeUnderstandingQueryService.ResolveProjectSelector(
            solution,
            request.ProjectPath,
            request.ProjectName,
            request.ProjectId,
            selectorRequired: false,
            toolName: "list_dependencies",
            out var selectorError);

        if (selectorProvided && selectorError != null)
        {
            return new ListDependenciesResult(
                Array.Empty<ProjectDependency>(),
                0,
                selectorError,
                Array.Empty<ProjectDependencyEdge>());
        }

        var targetProject = selectorProvided ? selectedProjects[0] : null;
        var edgeByKey = new Dictionary<string, ProjectDependencyEdge>(StringComparer.Ordinal);
        var dependencyById = new Dictionary<string, ProjectDependency>(StringComparer.Ordinal);

        if (targetProject != null)
        {
            if (direction == "outgoing" || direction == "both")
            {
                foreach (var reference in targetProject.ProjectReferences.OrderBy(static r => r.ProjectId.Id.ToString(), StringComparer.Ordinal))
                {
                    var dependencyProject = solution.GetProject(reference.ProjectId);
                    if (dependencyProject == null)
                    {
                        continue;
                    }

                    AddDependencyEdge(targetProject, dependencyProject, edgeByKey, dependencyById, counterpart: dependencyProject);
                }
            }

            if (direction == "incoming" || direction == "both")
            {
                foreach (var project in solution.Projects)
                {
                    if (project.ProjectReferences.Any(r => r.ProjectId == targetProject.Id))
                    {
                        AddDependencyEdge(project, targetProject, edgeByKey, dependencyById, counterpart: project);
                    }
                }
            }
        }
        else
        {
            var allReferenceEdges = new List<(Project Source, Project Target)>();
            foreach (var project in solution.Projects)
            {
                foreach (var reference in project.ProjectReferences)
                {
                    var dependencyProject = solution.GetProject(reference.ProjectId);
                    if (dependencyProject != null)
                    {
                        allReferenceEdges.Add((project, dependencyProject));
                    }
                }
            }

            if (direction == "outgoing" || direction == "both")
            {
                foreach (var (source, target) in allReferenceEdges)
                {
                    AddDependencyEdge(source, target, edgeByKey, dependencyById, counterpart: target);
                }
            }

            if (direction == "incoming" || direction == "both")
            {
                foreach (var (source, target) in allReferenceEdges)
                {
                    AddDependencyEdge(target, source, edgeByKey, dependencyById, counterpart: source);
                }
            }
        }

        var orderedEdges = edgeByKey.Values
            .OrderBy(static edge => edge.Source.ProjectName, StringComparer.Ordinal)
            .ThenBy(static edge => edge.Source.ProjectId, StringComparer.Ordinal)
            .ThenBy(static edge => edge.Target.ProjectName, StringComparer.Ordinal)
            .ThenBy(static edge => edge.Target.ProjectId, StringComparer.Ordinal)
            .ToArray();

        var dependencies = dependencyById.Values
            .OrderBy(static dependency => dependency.ProjectName, StringComparer.Ordinal)
            .ThenBy(static dependency => dependency.ProjectId, StringComparer.Ordinal)
            .ToArray();

        return new ListDependenciesResult(dependencies, dependencies.Length, null, orderedEdges);
    }

    private static void AddDependencyEdge(
        Project source,
        Project target,
        IDictionary<string, ProjectDependencyEdge> edgeByKey,
        IDictionary<string, ProjectDependency> dependencyById,
        Project counterpart)
    {
        var sourceDependency = ToProjectDependency(source);
        var targetDependency = ToProjectDependency(target);
        var edgeKey = $"{sourceDependency.ProjectId}->{targetDependency.ProjectId}";
        edgeByKey[edgeKey] = new ProjectDependencyEdge(sourceDependency, targetDependency);

        var counterpartDependency = ToProjectDependency(counterpart);
        dependencyById[counterpartDependency.ProjectId] = counterpartDependency;
    }

    private static ProjectDependency ToProjectDependency(Project project)
        => new(project.Name, project.Id.Id.ToString());
}
