using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models.Agent;
using RoslynMcp.Core.Models.Analysis;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Navigation;
using RoslynMcp.Infrastructure.Navigation;
using RoslynMcp.Infrastructure.Workspace;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Infrastructure.Agent;

internal sealed class CodeUnderstandingQueryService
{
    private readonly IRoslynSolutionAccessor _solutionAccessor;
    private readonly ISolutionSessionService _solutionSessionService;
    private readonly IWorkspaceBootstrapService _workspaceBootstrapService;
    private readonly ISymbolLookupService _symbolLookupService;
    private readonly INavigationService _navigationService;

    public CodeUnderstandingQueryService(
        IRoslynSolutionAccessor solutionAccessor,
        ISolutionSessionService solutionSessionService,
        IWorkspaceBootstrapService workspaceBootstrapService,
        ISymbolLookupService symbolLookupService,
        INavigationService navigationService)
    {
        _solutionAccessor = solutionAccessor;
        _solutionSessionService = solutionSessionService;
        _workspaceBootstrapService = workspaceBootstrapService;
        _symbolLookupService = symbolLookupService;
        _navigationService = navigationService;
    }

    public async Task<(Solution? Solution, ErrorInfo? Error)> GetCurrentSolutionWithAutoBootstrapAsync(
        string noSolutionNextAction,
        string? workspaceHintPath,
        CancellationToken ct)
    {
        var (solution, error) = await _solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
        if (solution != null)
        {
            return (solution, null);
        }

        var discoveryRoot = workspaceHintPath.ResolveDiscoveryRoot();
        var discovered = await _solutionSessionService
            .DiscoverSolutionsAsync(new DiscoverSolutionsRequest(discoveryRoot), ct)
            .ConfigureAwait(false);

        if (discovered.Error != null || discovered.SolutionPaths.Count != 1)
        {
            return (null, AgentErrorInfo.Normalize(error, noSolutionNextAction));
        }

        var load = await _workspaceBootstrapService
            .LoadSolutionAsync(new LoadSolutionRequest(discovered.SolutionPaths[0]), ct)
            .ConfigureAwait(false);

        if (load.Error != null)
        {
            return (null, AgentErrorInfo.Normalize(load.Error, noSolutionNextAction));
        }

        var (autoLoadedSolution, autoLoadedError) = await _solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
        if (autoLoadedSolution == null)
        {
            return (null, AgentErrorInfo.Normalize(autoLoadedError ?? error, noSolutionNextAction));
        }

        return (autoLoadedSolution, null);
    }

    public async Task<IReadOnlyList<HotspotSummary>> BuildHotspotsAsync(
        Solution solution,
        IReadOnlyList<MetricItem> metrics,
        int hotspotCount,
        CancellationToken ct)
    {
        var ranked = metrics
            .OrderByDescending(static m => m.CyclomaticComplexity ?? 0)
            .ThenByDescending(static m => m.LineCount ?? 0)
            .ThenBy(static m => m.SymbolId, StringComparer.Ordinal)
            .Take(hotspotCount)
            .ToArray();

        var hotspots = new List<HotspotSummary>(ranked.Length);
        foreach (var metric in ranked)
        {
            var complexity = metric.CyclomaticComplexity ?? 0;
            var lineCount = metric.LineCount ?? 0;
            var score = complexity + lineCount;

            var symbol = await _symbolLookupService.ResolveSymbolAsync(metric.SymbolId, solution, ct).ConfigureAwait(false);
            var displayName = symbol?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ?? metric.SymbolId;
            var (filePath, startLine, _, endLine, _) = symbol.GetSourceSpan();
            var reason = $"complexity={complexity}, lines={lineCount}";
            if (string.IsNullOrWhiteSpace(filePath))
            {
                reason += ", location=unknown";
            }

            hotspots.Add(new HotspotSummary(
                Label: displayName,
                Path: filePath,
                Reason: reason,
                Score: score,
                SymbolId: metric.SymbolId,
                DisplayName: displayName,
                FilePath: filePath,
                StartLine: startLine,
                EndLine: endLine,
                Complexity: complexity,
                LineCount: lineCount));
        }

        return hotspots
            .OrderByDescending(static h => h.Score)
            .ThenBy(static h => h.SymbolId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<GetSymbolAtPositionResult> ResolveSymbolAtRequestAsync(
        string? symbolId,
        string? path,
        int? line,
        int? column,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(symbolId))
        {
            var find = await _navigationService.FindSymbolAsync(new FindSymbolRequest(symbolId), ct).ConfigureAwait(false);
            return new GetSymbolAtPositionResult(find.Symbol, find.Error);
        }

        if (!string.IsNullOrWhiteSpace(path) && line.HasValue && column.HasValue)
        {
            return await _navigationService.GetSymbolAtPositionAsync(
                new GetSymbolAtPositionRequest(path, line.Value, column.Value),
                ct).ConfigureAwait(false);
        }

        return new GetSymbolAtPositionResult(
            null,
            AgentErrorInfo.Create(
                ErrorCodes.InvalidInput,
                "Provide symbolId or path/line/column.",
                "Call explain_symbol with a symbolId or source position."));
    }

    public async Task<(INamedTypeSymbol? Symbol, ErrorInfo? Error)> ResolveTypeSymbolAsync(
        ListMembersRequest request,
        Solution solution,
        CancellationToken ct)
    {
        var hasExplicitTypeSymbolId = !string.IsNullOrWhiteSpace(request.TypeSymbolId);
        ISymbol? symbol;

        if (hasExplicitTypeSymbolId)
        {
            symbol = await _symbolLookupService.ResolveSymbolAsync(request.TypeSymbolId!, solution, ct).ConfigureAwait(false);
            if (symbol == null)
            {
                return (null,
                    AgentErrorInfo.Create(
                        ErrorCodes.InvalidInput,
                        $"typeSymbolId '{request.TypeSymbolId}' could not be resolved.",
                        "Call list_types first to select a valid type symbolId.",
                        ("field", "typeSymbolId"),
                        ("provided", request.TypeSymbolId),
                        ("expected", "type symbolId returned by list_types")));
            }

            if (symbol is not INamedTypeSymbol namedType)
            {
                return (null,
                    AgentErrorInfo.Create(
                        ErrorCodes.InvalidInput,
                        "typeSymbolId must resolve to a type symbol.",
                        "Call list_types and pass a type symbolId, not a member symbolId.",
                        ("field", "typeSymbolId"),
                        ("provided", request.TypeSymbolId),
                        ("expected", "type symbolId")));
            }

            return (namedType, null);
        }

        if (!string.IsNullOrWhiteSpace(request.Path) && request.Line.HasValue && request.Column.HasValue)
        {
            symbol = await _symbolLookupService
                .GetSymbolAtPositionAsync(solution, request.Path!, request.Line.Value, request.Column.Value, ct)
                .ConfigureAwait(false);
            if (symbol == null)
            {
                return (null,
                    AgentErrorInfo.Create(
                        ErrorCodes.SymbolNotFound,
                        "No symbol found at the provided source position.",
                        "Call list_members with a valid typeSymbolId from list_types, or provide a valid source position."));
            }
        }
        else
        {
            return (null,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "Provide typeSymbolId or path/line/column.",
                    "Call list_members with a typeSymbolId from list_types, or provide a source position."));
        }

        var typeSymbol = symbol switch
        {
            INamedTypeSymbol namedType => namedType,
            _ => symbol.ContainingType
        };

        if (typeSymbol == null)
        {
            return (null,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "Resolved symbol is not a type and has no containing type.",
                    "Call list_members with a symbolId that resolves to a type declaration.",
                    ("field", "typeSymbolId")));
        }

        return (typeSymbol, null);
    }

    public async Task<FindUnusedSymbolsResult> FindUnusedSymbolsAsync(
        FindUnusedSymbolsRequest request,
        Solution solution,
        CancellationToken ct)
    {
        var warnings = new List<string>();

        // Resolve project from request parameters
        Project? project = null;
        if (!string.IsNullOrWhiteSpace(request.ProjectPath))
        {
            project = solution.Projects.FirstOrDefault(p =>
                p.FilePath?.Equals(request.ProjectPath, StringComparison.OrdinalIgnoreCase) == true);
        }
        else if (!string.IsNullOrWhiteSpace(request.ProjectName))
        {
            project = solution.Projects.FirstOrDefault(p =>
                p.Name.Equals(request.ProjectName, StringComparison.OrdinalIgnoreCase));
        }
        else if (!string.IsNullOrWhiteSpace(request.ProjectId))
        {
            project = solution.GetProject(ProjectId.CreateFromSerialized(Guid.Parse(request.ProjectId)));
        }
        else
        {
            // Default to first project with source files
            project = solution.Projects.FirstOrDefault(p => p.SupportsCompilation);
        }

        if (project == null)
        {
            return new FindUnusedSymbolsResult(
                Array.Empty<UnusedSymbolEntry>(),
                0,
                0,
                warnings,
                AgentErrorInfo.Create(
                    ErrorCodes.InvalidInput,
                    "Project not found.",
                    "Specify a valid project using projectPath, projectName, or projectId."));
        }

        var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
        if (compilation == null)
        {
            return new FindUnusedSymbolsResult(
                Array.Empty<UnusedSymbolEntry>(),
                0,
                0,
                warnings,
                AgentErrorInfo.Create(
                    ErrorCodes.AnalysisFailed,
                    "Failed to compile project.",
                    "Check for compilation errors."));
        }

        var threshold = request.MinReferenceCount ?? 0;
        var targetAccessibility = request.Accessibility?.ToLowerInvariant() ?? "all";
        var targetKind = request.Kind?.ToLowerInvariant() ?? "all";

        var symbols = new List<UnusedSymbolEntry>();
        var publicApiCount = 0;

        // Collect all candidate symbols from the project
        var candidateSymbols = new List<ISymbol>();
        foreach (var document in project.Documents.Where(d => d.SupportsSyntaxTree))
        {
            var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
            if (semanticModel == null) continue;

            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (root == null) continue;

            foreach (var node in root.DescendantNodes())
            {
                ISymbol? symbol = null;

                switch (node)
                {
                    case Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax method:
                        if (method.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.OverrideKeyword) ||
                                                       m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.VirtualKeyword) ||
                                                       m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AbstractKeyword)))
                            continue;
                        symbol = semanticModel.GetDeclaredSymbol(method, ct);
                        if (!MatchesKind(symbol, targetKind)) continue;
                        if (IsEntryPoint(symbol)) continue; // Main method is always "used"
                        break;

                    case Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax prop:
                        symbol = semanticModel.GetDeclaredSymbol(prop, ct);
                        if (!MatchesKind(symbol, targetKind)) continue;
                        break;

                    case Microsoft.CodeAnalysis.CSharp.Syntax.FieldDeclarationSyntax field:
                        foreach (var variable in field.Declaration.Variables)
                        {
                            var fieldSymbol = semanticModel.GetDeclaredSymbol(variable, ct);
                            if (fieldSymbol != null && MatchesKind(fieldSymbol, targetKind) && MatchesAccessibility(fieldSymbol, targetAccessibility))
                            {
                                candidateSymbols.Add(fieldSymbol);
                            }
                        }
                        continue;

                    case Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax cls:
                    case Microsoft.CodeAnalysis.CSharp.Syntax.StructDeclarationSyntax str:
                    case Microsoft.CodeAnalysis.CSharp.Syntax.InterfaceDeclarationSyntax iface:
                    case Microsoft.CodeAnalysis.CSharp.Syntax.EnumDeclarationSyntax enm:
                    case Microsoft.CodeAnalysis.CSharp.Syntax.RecordDeclarationSyntax rec:
                        symbol = semanticModel.GetDeclaredSymbol(node, ct);
                        if (!MatchesKind(symbol, targetKind)) continue;
                        // Skip types with public entry points or attributes suggesting serialization/framework usage
                        if (HasFrameworkAttributes(symbol)) continue;
                        break;
                }

                if (symbol != null && MatchesAccessibility(symbol, targetAccessibility))
                {
                    candidateSymbols.Add(symbol);
                }
            }
        }

        // Check references for each candidate
        foreach (var symbol in candidateSymbols.Distinct(SymbolEqualityComparer.Default))
        {
            if (symbol == null) continue;

            var references = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder
                .FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);

            var refCount = references.Sum(r => r.Locations.Count());

            // If it's defined in the project but referenced elsewhere, those refs count too
            // The above only finds refs within the solution

            if (refCount <= threshold)
            {
                var isPublicApi = symbol.DeclaredAccessibility == Accessibility.Public ||
                                  symbol.DeclaredAccessibility == Accessibility.Protected;

                if (isPublicApi)
                    publicApiCount++;

                var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
                var lineSpan = location?.GetLineSpan();

                symbols.Add(new UnusedSymbolEntry(
                    SymbolIdentity.CreateId(symbol),
                    symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    symbol.Kind.ToString(),
                    symbol.DeclaredAccessibility.ToString(),
                    location?.SourceTree?.FilePath ?? string.Empty,
                    lineSpan?.StartLinePosition.Line + 1 ?? 0,
                    lineSpan?.StartLinePosition.Character + 1 ?? 0,
                    refCount,
                    isPublicApi));
            }
        }

        // Sort by reference count, then by name
        var sortedSymbols = symbols
            .OrderBy(s => s.ReferenceCount)
            .ThenBy(s => s.DisplayName, StringComparer.Ordinal)
            .ToArray();

        return new FindUnusedSymbolsResult(
            sortedSymbols,
            sortedSymbols.Length,
            publicApiCount,
            warnings,
            null);

        static bool MatchesKind(ISymbol? symbol, string kind)
        {
            if (symbol == null) return false;
            if (kind == "all") return true;

            return kind switch
            {
                "method" => symbol.Kind == SymbolKind.Method,
                "property" => symbol.Kind == SymbolKind.Property,
                "field" => symbol.Kind == SymbolKind.Field,
                "event" => symbol.Kind == SymbolKind.Event,
                "type" => symbol is INamedTypeSymbol,
                _ => true
            };
        }

        static bool MatchesAccessibility(ISymbol? symbol, string accessibility)
        {
            if (symbol == null) return false;
            if (accessibility == "all") return true;

            return accessibility switch
            {
                "public" => symbol.DeclaredAccessibility == Accessibility.Public,
                "internal" => symbol.DeclaredAccessibility == Accessibility.Internal,
                "protected" => symbol.DeclaredAccessibility == Accessibility.Protected,
                "private" => symbol.DeclaredAccessibility == Accessibility.Private,
                "protected_internal" => symbol.DeclaredAccessibility == Accessibility.ProtectedOrInternal,
                "private_protected" => symbol.DeclaredAccessibility == Accessibility.ProtectedAndInternal,
                _ => true
            };
        }

        static bool IsEntryPoint(ISymbol? symbol)
        {
            if (symbol is not IMethodSymbol method) return false;
            return method.Name == "Main" &&
                   method.IsStatic &&
                   (method.DeclaredAccessibility == Accessibility.Public ||
                    method.DeclaredAccessibility == Accessibility.Internal);
        }

        static bool HasFrameworkAttributes(ISymbol? symbol)
        {
            if (symbol == null) return false;
            // Types with certain attributes are likely used by frameworks even if no direct refs
            var attrNames = new[] { "JsonConverter", "TypeConverter", "DataContract", "Serializable", "ApiController" };
            return symbol.GetAttributes().Any(a =>
                attrNames.Any(name => a.AttributeClass?.Name.Contains(name) == true));
        }
    }
}
