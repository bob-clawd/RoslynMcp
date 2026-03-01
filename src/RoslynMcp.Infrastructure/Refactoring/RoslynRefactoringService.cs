using RoslynMcp.Core.Contracts;
using RoslynMcp.Core;
using RoslynMcp.Core.Models.Common;
using RoslynMcp.Core.Models.Refactoring;
using RoslynMcp.Infrastructure.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace RoslynMcp.Infrastructure.Refactoring;

internal sealed partial class RefactoringOperationOrchestrator : IRefactoringOperationOrchestrator
{
    private const string SupportedFixOperation = "remove_unused_local";
    private const string CleanupRuleRemoveUnusedUsings = "remove_unused_usings";
    private const string CleanupRuleOrganizeUsings = "organize_usings";
    private const string CleanupRuleFixModifierOrder = "fix_modifier_order";
    private const string CleanupRuleAddReadonly = "add_readonly";
    private const string CleanupRuleFormat = "format";
    private const string CleanupHealthCheckPerformedDetail = "healthCheckPerformed";
    private const string CleanupAutoReloadAttemptedDetail = "autoReloadAttempted";
    private const string CleanupAutoReloadSucceededDetail = "autoReloadSucceeded";
    private const string CleanupMissingFileCountDetail = "missingFileCount";
    private const string CleanupReloadErrorCodeDetail = "reloadErrorCode";
    private const string CleanupStaleWorkspaceMessage = "Workspace snapshot is stale relative to filesystem. Run reload_solution or load_solution, then retry cleanup.";
    private const string SupportedFixCategory = "compiler";
    private const string RefactoringOperationUseVar = "use_var_for_local";
    private const string OriginRoslynatorCodeFix = "roslynator_codefix";
    private const string OriginRoslynatorRefactoring = "roslynator_refactoring";
    private const string PolicyProfileDefault = "default";
    private const string RefactoringCategoryDefault = "refactoring";
    private const string RefactoringActionPipelineFlowLog = "refactoring_action_pipeline_flow";

    private static readonly HashSet<string> SupportedFixDiagnosticIds =
        new(StringComparer.OrdinalIgnoreCase) { "CS0168", "CS0219", "IDE0059" };

    private static readonly HashSet<string> CleanupRemoveUnusedUsingDiagnostics =
        new(StringComparer.OrdinalIgnoreCase) { "IDE0005", "CS8019" };

    private static readonly HashSet<string> CleanupModifierOrderDiagnostics =
        new(StringComparer.OrdinalIgnoreCase) { "IDE0036" };

    private static readonly HashSet<string> CleanupReadonlyDiagnostics =
        new(StringComparer.OrdinalIgnoreCase) { "IDE0044" };

    private readonly IRoslynSolutionAccessor _solutionAccessor;
    private readonly ILogger<RoslynRefactoringService> _logger;
    private readonly ActionIdentityService _actionIdentityService;
    private readonly RefactoringPolicyService _refactoringPolicyService;
    private readonly RoslynatorProviderCatalogService _providerCatalogService;
    private readonly RefactoringActionOperations _refactoringActions;
    private readonly CodeFixOperations _codeFixOperations;
    private readonly CleanupOperations _cleanupOperations;
    private readonly RenameOperations _renameOperations;

    public RefactoringOperationOrchestrator(IRoslynSolutionAccessor solutionAccessor,
        ILogger<RoslynRefactoringService>? logger = null)
    {
        _solutionAccessor = solutionAccessor ?? throw new ArgumentNullException(nameof(solutionAccessor));
        _logger = logger ?? NullLogger<RoslynRefactoringService>.Instance;
        _actionIdentityService = new ActionIdentityService();
        _refactoringPolicyService = new RefactoringPolicyService();
        _providerCatalogService = new RoslynatorProviderCatalogService();
        _refactoringActions = new RefactoringActionOperations(this);
        _codeFixOperations = new CodeFixOperations(this);
        _cleanupOperations = new CleanupOperations(this);
        _renameOperations = new RenameOperations(this);
    }

    public Task<GetRefactoringsAtPositionResult> GetRefactoringsAtPositionAsync(
        GetRefactoringsAtPositionRequest request,
        CancellationToken ct)
        => _refactoringActions.GetRefactoringsAtPositionAsync(request, ct);

    public Task<PreviewRefactoringResult> PreviewRefactoringAsync(PreviewRefactoringRequest request, CancellationToken ct)
        => _refactoringActions.PreviewRefactoringAsync(request, ct);

    public Task<ApplyRefactoringResult> ApplyRefactoringAsync(ApplyRefactoringRequest request, CancellationToken ct)
        => _refactoringActions.ApplyRefactoringAsync(request, ct);

    public Task<GetCodeFixesResult> GetCodeFixesAsync(GetCodeFixesRequest request, CancellationToken ct)
        => _codeFixOperations.GetCodeFixesAsync(request, ct);

    public Task<PreviewCodeFixResult> PreviewCodeFixAsync(PreviewCodeFixRequest request, CancellationToken ct)
        => _codeFixOperations.PreviewCodeFixAsync(request, ct);

    public Task<ApplyCodeFixResult> ApplyCodeFixAsync(ApplyCodeFixRequest request, CancellationToken ct)
        => _codeFixOperations.ApplyCodeFixAsync(request, ct);

    public Task<ExecuteCleanupResult> ExecuteCleanupAsync(ExecuteCleanupRequest request, CancellationToken ct)
        => _cleanupOperations.ExecuteCleanupAsync(request, ct);

    private static ExecuteCleanupResult CreateStaleWorkspaceResult(
        string scope,
        bool healthCheckPerformed,
        bool autoReloadAttempted,
        bool autoReloadSucceeded,
        int missingFileCount,
        string? reloadErrorCode = null)
    {
        return new ExecuteCleanupResult(
            scope,
            Array.Empty<string>(),
            Array.Empty<string>(),
            BuildCleanupMetadataWarnings(healthCheckPerformed, autoReloadAttempted, autoReloadSucceeded),
            CreateError(
                ErrorCodes.StaleWorkspaceSnapshot,
                CleanupStaleWorkspaceMessage,
                ("operation", "execute_cleanup"),
                (CleanupHealthCheckPerformedDetail, BoolToLowerInvariantString(healthCheckPerformed)),
                (CleanupAutoReloadAttemptedDetail, BoolToLowerInvariantString(autoReloadAttempted)),
                (CleanupAutoReloadSucceededDetail, BoolToLowerInvariantString(autoReloadSucceeded)),
                (CleanupMissingFileCountDetail, missingFileCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                (CleanupReloadErrorCodeDetail, reloadErrorCode)));
    }

    private static IReadOnlyList<string> BuildCleanupMetadataWarnings(bool healthCheckPerformed, bool autoReloadAttempted, bool autoReloadSucceeded)
        =>
        [
            $"meta.{CleanupHealthCheckPerformedDetail}={BoolToLowerInvariantString(healthCheckPerformed)}",
            $"meta.{CleanupAutoReloadAttemptedDetail}={BoolToLowerInvariantString(autoReloadAttempted)}",
            $"meta.{CleanupAutoReloadSucceededDetail}={BoolToLowerInvariantString(autoReloadSucceeded)}"
        ];

    private static CleanupWorkspaceHealth EvaluateWorkspaceFilesystemHealth(IReadOnlyList<Document> scopedDocuments)
    {
        var missingRootedFiles = scopedDocuments
            .Select(static document => document.FilePath)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path!)
            .Where(static filePath => Path.IsPathRooted(filePath))
            .Where(static path => !File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CleanupWorkspaceHealth(missingRootedFiles.Length == 0, missingRootedFiles);
    }

    private static string BoolToLowerInvariantString(bool value)
        => value ? "true" : "false";

    private sealed record CleanupWorkspaceHealth(bool IsConsistent, IReadOnlyList<string> MissingRootedFiles);

    public Task<RenameSymbolResult> RenameSymbolAsync(RenameSymbolRequest request, CancellationToken ct)
        => _renameOperations.RenameSymbolAsync(request, ct);

    private static RenameSymbolResult CreateErrorResult(string code, string message, params (string Key, string? Value)[] details)
        => new(null, 0, Array.Empty<SourceLocation>(), Array.Empty<string>(), CreateError(code, message, details));

    private static IReadOnlyList<string> BuildCleanupRuleIds()
        =>
        [
            CleanupRuleRemoveUnusedUsings,
            CleanupRuleOrganizeUsings,
            CleanupRuleFixModifierOrder,
            CleanupRuleAddReadonly,
            CleanupRuleFormat
        ];

    private async Task<Solution> ApplyDiagnosticCleanupStepAsync(
        Solution solution,
        IReadOnlyList<Document> scopeDocuments,
        ISet<string> allowedDiagnosticIds,
        CancellationToken ct)
    {
        var updated = solution;
        for (var pass = 0; pass < 3; pass++)
        {
            var changedInPass = false;
            foreach (var baseDocument in scopeDocuments)
            {
                ct.ThrowIfCancellationRequested();
                var document = updated.GetDocument(baseDocument.Id);
                if (document == null)
                {
                    continue;
                }

                var diagnostics = await GetProviderDiagnosticsForDocumentAsync(document, ct).ConfigureAwait(false);
                var candidates = diagnostics
                    .Where(d => d.Location.IsInSource && allowedDiagnosticIds.Contains(d.Id))
                    .OrderBy(static d => d.Location.GetLineSpan().Path, StringComparer.Ordinal)
                    .ThenBy(static d => d.Location.SourceSpan.Start)
                    .ThenBy(static d => d.Location.SourceSpan.Length)
                    .ThenBy(static d => d.Id, StringComparer.Ordinal)
                    .ToArray();

                foreach (var diagnostic in candidates)
                {
                    var actions = await CollectCodeFixActionsAsync(document, diagnostic, ct).ConfigureAwait(false);
                    var action = actions
                        .OrderBy(static candidate => candidate.ProviderTypeName, StringComparer.Ordinal)
                        .ThenBy(static candidate => candidate.Action.Title, StringComparer.Ordinal)
                        .ThenBy(static candidate => candidate.Action.EquivalenceKey ?? string.Empty, StringComparer.Ordinal)
                        .Select(static candidate => candidate.Action)
                        .FirstOrDefault();
                    if (action == null)
                    {
                        continue;
                    }

                    var applied = await TryApplyCodeActionToSolutionAsync(updated, action, ct).ConfigureAwait(false);
                    if (applied == null)
                    {
                        continue;
                    }

                    updated = applied;
                    document = updated.GetDocument(baseDocument.Id);
                    changedInPass = true;
                }
            }

            if (!changedInPass)
            {
                break;
            }
        }

        return updated;
    }

    private async Task<Solution> OrganizeUsingsAsync(Solution solution, IReadOnlyList<Document> scopeDocuments, CancellationToken ct)
    {
        var updated = solution;
        foreach (var baseDocument in scopeDocuments)
        {
            ct.ThrowIfCancellationRequested();
            var document = updated.GetDocument(baseDocument.Id);
            if (document == null)
            {
                continue;
            }

            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            if (root is not CompilationUnitSyntax compilationUnit)
            {
                continue;
            }

            var organizedRoot = OrganizeUsings(compilationUnit);
            if (organizedRoot.IsEquivalentTo(compilationUnit))
            {
                continue;
            }

            updated = updated.WithDocumentSyntaxRoot(document.Id, organizedRoot);
        }

        return updated;
    }

    private static CompilationUnitSyntax OrganizeUsings(CompilationUnitSyntax root)
    {
        var updated = root.WithUsings(SortUsingDirectives(root.Usings));
        foreach (var namespaceDeclaration in updated.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
        {
            var orderedUsings = SortUsingDirectives(namespaceDeclaration.Usings);
            if (orderedUsings == namespaceDeclaration.Usings)
            {
                continue;
            }

            updated = updated.ReplaceNode(namespaceDeclaration, namespaceDeclaration.WithUsings(orderedUsings));
        }

        return updated;
    }

    private static SyntaxList<UsingDirectiveSyntax> SortUsingDirectives(SyntaxList<UsingDirectiveSyntax> usings)
    {
        if (usings.Count <= 1)
        {
            return usings;
        }

        return SyntaxFactory.List(
            usings
                .OrderBy(static directive => directive.Alias == null ? 1 : 0)
                .ThenBy(static directive => directive.StaticKeyword.IsKind(SyntaxKind.StaticKeyword) ? 1 : 0)
                .ThenBy(static directive => directive.Name?.ToString(), StringComparer.Ordinal)
                .ThenBy(static directive => directive.Alias?.Name.Identifier.ValueText ?? string.Empty, StringComparer.Ordinal));
    }

    private async Task<Solution> FormatScopeAsync(Solution solution, IReadOnlyList<Document> scopeDocuments, CancellationToken ct)
    {
        var updated = solution;
        foreach (var baseDocument in scopeDocuments)
        {
            ct.ThrowIfCancellationRequested();
            var document = updated.GetDocument(baseDocument.Id);
            if (document == null)
            {
                continue;
            }

            var formatted = await Formatter.FormatAsync(document, cancellationToken: ct).ConfigureAwait(false);
            updated = formatted.Project.Solution;
        }

        return updated;
    }

    private static RenameSymbolResult CreateErrorResult(ErrorInfo? error)
    {
        var safeError = error ?? new ErrorInfo(ErrorCodes.InternalError, "An unknown error occurred while renaming a symbol.");
        return new RenameSymbolResult(null, 0, Array.Empty<SourceLocation>(), Array.Empty<string>(), safeError);
    }

    private static ErrorInfo CreateError(string code, string message, params (string Key, string? Value)[] details)
    {
        if (details.Length == 0)
        {
            return new ErrorInfo(code, message);
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in details)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                map[key] = value;
            }
        }

        return map.Count == 0 ? new ErrorInfo(code, message) : new ErrorInfo(code, message, map);
    }

    private static ErrorInfo? TryCreateInvalidSymbolIdError(string symbolId, string operation)
    {
        if (!string.IsNullOrWhiteSpace(symbolId))
        {
            return null;
        }

        return CreateError(
            ErrorCodes.InvalidInput,
            "symbolId must be a non-empty, non-whitespace string.",
            ("parameter", "symbolId"),
            ("operation", operation));
    }

    private async Task<(Solution? Solution, ErrorInfo? Error)> TryGetSolutionAsync(CancellationToken ct)
    {
        try
        {
            return await _solutionAccessor.GetCurrentSolutionAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to access solution state for rename");
            return (null, new ErrorInfo(ErrorCodes.InternalError, "Unable to access the current solution."));
        }
    }

    private static bool IsValidIdentifierForSymbol(ISymbol symbol, string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (symbol.Language == LanguageNames.CSharp)
        {
            return SyntaxFacts.IsValidIdentifier(candidate);
        }

        return true;
    }

    private static SourceLocation CreateSourceLocation(Location location)
    {
        var span = location.GetLineSpan();
        var filePath = span.Path ?? string.Empty;
        var start = span.StartLinePosition;
        return new SourceLocation(filePath, start.Line + 1, start.Character + 1);
    }

    private static string GetLocationKey(SourceLocation location)
        => string.Join(':', location.FilePath, location.Line, location.Column);

    private static async Task<IReadOnlyList<SourceLocation>> CollectAffectedLocationsAsync(ISymbol symbol, Solution solution, CancellationToken ct)
    {
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var locations = new List<SourceLocation>();

        foreach (var reference in references)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var location in reference.Locations)
            {
                if (!location.Location.IsInSource)
                {
                    continue;
                }

                var sourceLocation = CreateSourceLocation(location.Location);
                var key = GetLocationKey(sourceLocation);
                if (seen.Add(key))
                {
                    locations.Add(sourceLocation);
                }
            }
        }

        foreach (var location in symbol.Locations.Where(l => l.IsInSource))
        {
            var sourceLocation = CreateSourceLocation(location);
            var key = GetLocationKey(sourceLocation);
            if (seen.Add(key))
            {
                locations.Add(sourceLocation);
            }
        }

        return locations
            .OrderBy(loc => loc.FilePath, StringComparer.Ordinal)
            .ThenBy(loc => loc.Line)
            .ThenBy(loc => loc.Column)
            .ToList();
    }

    private async Task<ISymbol?> ResolveSymbolAsync(string symbolId, Solution solution, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbolId))
        {
            return null;
        }

        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation == null)
            {
                continue;
            }

            var resolved = SymbolIdentity.Resolve(symbolId, compilation, ct);
            if (resolved != null)
            {
                return resolved.OriginalDefinition ?? resolved;
            }
        }

        return null;
    }

    private static ISet<string> GetSourceLocationKeys(ISymbol symbol)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var location in symbol.Locations.Where(static l => l.IsInSource))
        {
            var sourceLocation = CreateSourceLocation(location);
            keys.Add(GetLocationKey(sourceLocation));
        }

        return keys;
    }

    private async Task<ISymbol?> TryResolveRenamedSymbolAsync(Solution solution,
        string newName,
        ISet<string> originalDeclarationKeys,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newName) || originalDeclarationKeys.Count == 0)
        {
            return null;
        }

        foreach (var project in solution.Projects)
        {
            ct.ThrowIfCancellationRequested();
            var candidates = await SymbolFinder.FindDeclarationsAsync(project, newName, ignoreCase: false,
                    SymbolFilter.TypeAndMember, ct)
                .ConfigureAwait(false);

            foreach (var candidate in candidates)
            {
                var normalizedCandidate = candidate.OriginalDefinition ?? candidate;
                foreach (var sourceLocation in normalizedCandidate.Locations.Where(static l => l.IsInSource)
                             .Select(CreateSourceLocation))
                {
                    if (originalDeclarationKeys.Contains(GetLocationKey(sourceLocation)))
                    {
                        return normalizedCandidate;
                    }
                }
            }
        }

        return null;
    }

    private static bool WouldConflict(ISymbol symbol, string newName)
    {
        if (string.Equals(symbol.Name, newName, StringComparison.Ordinal))
        {
            return false;
        }

        var members = symbol.ContainingType?.GetMembers(newName) ?? default;
        if (members.IsDefaultOrEmpty && symbol.ContainingNamespace != null)
        {
            members = symbol.ContainingNamespace.GetMembers(newName)
                .Cast<ISymbol>()
                .ToImmutableArray();
        }

        if (members.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var member in members)
        {
            if (SymbolConflicts(symbol, member))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SymbolConflicts(ISymbol original, ISymbol existing)
    {
        var normalizedOriginal = original.OriginalDefinition ?? original;
        var normalizedExisting = existing.OriginalDefinition ?? existing;

        if (SymbolEqualityComparer.Default.Equals(normalizedOriginal, normalizedExisting))
        {
            return false;
        }

        if (normalizedOriginal.Kind != normalizedExisting.Kind)
        {
            return false;
        }

        if (normalizedOriginal is IMethodSymbol originalMethod && normalizedExisting is IMethodSymbol existingMethod)
        {
            if (originalMethod.Parameters.Length != existingMethod.Parameters.Length)
            {
                return false;
            }

            for (var i = 0; i < originalMethod.Parameters.Length; i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(originalMethod.Parameters[i].Type, existingMethod.Parameters[i].Type))
                {
                    return false;
                }
            }

            return true;
        }

        if (normalizedOriginal is IPropertySymbol && normalizedExisting is IPropertySymbol)
        {
            return true;
        }

        if (normalizedOriginal is IFieldSymbol && normalizedExisting is IFieldSymbol)
        {
            return true;
        }

        if (normalizedOriginal is IEventSymbol && normalizedExisting is IEventSymbol)
        {
            return true;
        }

        if (normalizedOriginal is INamedTypeSymbol && normalizedExisting is INamedTypeSymbol)
        {
            return true;
        }

        return true;
    }

    private static bool IsValidScope(string scope)
        => string.Equals(scope, "document", StringComparison.Ordinal)
           || string.Equals(scope, "project", StringComparison.Ordinal)
           || string.Equals(scope, "solution", StringComparison.Ordinal);

    private async Task<(Solution? Solution, int Version, ErrorInfo? Error)> TryGetSolutionWithVersionAsync(CancellationToken ct)
    {
        var (solution, solutionError) = await TryGetSolutionAsync(ct).ConfigureAwait(false);
        if (solution == null)
        {
            return (null, 0, solutionError);
        }

        try
        {
            var (version, versionError) = await _solutionAccessor.GetWorkspaceVersionAsync(ct).ConfigureAwait(false);
            if (versionError != null)
            {
                return (null, 0, versionError);
            }

            return (solution, version, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read workspace version");
            return (null, 0, new ErrorInfo(ErrorCodes.InternalError, "Unable to access workspace version."));
        }
    }

    private static IEnumerable<Document> ResolveScopeDocuments(Solution solution, string scope, string? path)
    {
        if (string.Equals(scope, "solution", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(path))
        {
            return solution.Projects.SelectMany(static project => project.Documents);
        }

        if (string.Equals(scope, "project", StringComparison.Ordinal))
        {
            return solution.Projects
                .Where(project => MatchesByNormalizedPath(project.FilePath, path)
                                  || string.Equals(project.Name, path, StringComparison.OrdinalIgnoreCase))
                .SelectMany(static project => project.Documents);
        }

        return solution.Projects
            .SelectMany(static project => project.Documents)
            .Where(document => MatchesByNormalizedPath(document.FilePath, path));
    }

    private static bool MatchesByNormalizedPath(string? candidatePath, string path)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var normalizedCandidate = System.IO.Path.GetFullPath(candidatePath);
            var normalizedPath = System.IO.Path.GetFullPath(path);
            return string.Equals(normalizedCandidate, normalizedPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return string.Equals(candidatePath, path, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static HashSet<string>? CreateDiagnosticFilter(IReadOnlyList<string>? diagnosticIds)
    {
        if (diagnosticIds == null || diagnosticIds.Count == 0)
        {
            return null;
        }

        var filter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in diagnosticIds)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                filter.Add(id.Trim());
            }
        }

        return filter.Count == 0 ? null : filter;
    }

    private static bool IsSupportedDiagnostic(Diagnostic diagnostic)
        => SupportedFixDiagnosticIds.Contains(diagnostic.Id);

    private static async Task<LocalDeclarationStatementSyntax?> TryGetUnusedLocalDeclarationAsync(
        Document document,
        Diagnostic diagnostic,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root == null)
        {
            return null;
        }

        var token = root.FindToken(diagnostic.Location.SourceSpan.Start);
        var declaration = token.Parent?.AncestorsAndSelf().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
        if (declaration == null)
        {
            return null;
        }

        if (declaration.Declaration.Variables.Count != 1)
        {
            return null;
        }

        return declaration;
    }

    private static CodeFixDescriptor CreateFixDescriptor(
        Document document,
        Diagnostic diagnostic,
        LocalDeclarationStatementSyntax declaration,
        int workspaceVersion)
    {
        var location = CreateSourceLocation(diagnostic.Location);
        var variableName = declaration.Declaration.Variables[0].Identifier.ValueText;
        var title = $"Remove unused local variable '{variableName}'";
        var filePath = document.FilePath ?? document.Name;
        var fixId = BuildFixId(workspaceVersion, diagnostic.Id, declaration.Span.Start, declaration.Span.Length, filePath);
        return new CodeFixDescriptor(fixId, title, diagnostic.Id, SupportedFixCategory, location, filePath);
    }

    private static string BuildFixId(int workspaceVersion, string diagnosticId, int spanStart, int spanLength, string filePath)
    {
        var encodedPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(filePath));
        return string.Join('|', "v1", workspaceVersion, SupportedFixOperation, diagnosticId, spanStart, spanLength, encodedPath);
    }

    private static ParsedFixId? ParseFixId(string fixId)
    {
        if (string.IsNullOrWhiteSpace(fixId))
        {
            return null;
        }

        var parts = fixId.Split('|');
        if (parts.Length != 7)
        {
            return null;
        }

        if (!string.Equals(parts[0], "v1", StringComparison.Ordinal)
            || !string.Equals(parts[2], SupportedFixOperation, StringComparison.Ordinal)
            || !int.TryParse(parts[1], out var version)
            || !int.TryParse(parts[4], out var spanStart)
            || !int.TryParse(parts[5], out var spanLength))
        {
            return null;
        }

        string filePath;
        try
        {
            filePath = Encoding.UTF8.GetString(Convert.FromBase64String(parts[6]));
        }
        catch (FormatException)
        {
            return null;
        }

        return new ParsedFixId(version, parts[3], spanStart, spanLength, filePath);
    }

    private async Task<FixOperation?> TryBuildFixOperationAsync(Solution solution, ParsedFixId fix, CancellationToken ct)
    {
        if (!SupportedFixDiagnosticIds.Contains(fix.DiagnosticId))
        {
            return null;
        }

        var document = solution.Projects
            .SelectMany(static project => project.Documents)
            .FirstOrDefault(d => MatchesByNormalizedPath(d.FilePath, fix.FilePath));
        if (document == null)
        {
            return null;
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root == null)
        {
            return null;
        }

        var declaration = root.FindNode(new TextSpan(fix.SpanStart, fix.SpanLength)) as LocalDeclarationStatementSyntax;
        if (declaration == null)
        {
            return null;
        }

        var variableName = declaration.Declaration.Variables[0].Identifier.ValueText;
        var title = $"Remove unused local variable '{variableName}'";
        return new FixOperation(
            title,
            async (currentSolution, cancellationToken) =>
            {
                var currentDocument = currentSolution.GetDocument(document.Id);
                if (currentDocument == null)
                {
                    return currentSolution;
                }

                var currentRoot = await currentDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (currentRoot == null)
                {
                    return currentSolution;
                }

                var currentDeclaration = currentRoot.FindNode(new TextSpan(fix.SpanStart, fix.SpanLength)) as LocalDeclarationStatementSyntax;
                if (currentDeclaration == null)
                {
                    return currentSolution;
                }

                var updatedRoot = currentRoot.RemoveNode(currentDeclaration, SyntaxRemoveOptions.KeepNoTrivia);
                if (updatedRoot == null)
                {
                    return currentSolution;
                }

                return currentSolution.WithDocumentSyntaxRoot(document.Id, updatedRoot);
            });
    }

    private static async Task<IReadOnlyList<ChangedFilePreview>> CollectChangedFilesAsync(Solution original, Solution updated, CancellationToken ct)
    {
        var changedDocumentIds = updated.GetChanges(original)
            .GetProjectChanges()
            .SelectMany(static project => project.GetChangedDocuments())
            .Distinct()
            .ToArray();

        var changed = new List<ChangedFilePreview>(changedDocumentIds.Length);
        foreach (var documentId in changedDocumentIds)
        {
            ct.ThrowIfCancellationRequested();
            var originalDoc = original.GetDocument(documentId);
            var updatedDoc = updated.GetDocument(documentId);
            var filePath = updatedDoc?.FilePath ?? updatedDoc?.Name ?? originalDoc?.FilePath ?? originalDoc?.Name ?? string.Empty;
            var editCount = 0;
            if (originalDoc != null && updatedDoc != null)
            {
                var originalText = await originalDoc.GetTextAsync(ct).ConfigureAwait(false);
                var updatedText = await updatedDoc.GetTextAsync(ct).ConfigureAwait(false);
                editCount = updatedText.GetTextChanges(originalText).Count;
            }

            changed.Add(new ChangedFilePreview(filePath, editCount));
        }

        return changed
            .Where(static file => !string.IsNullOrWhiteSpace(file.FilePath))
            .OrderBy(static file => file.FilePath, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<IReadOnlyList<DiscoveredAction>> DiscoverActionsAtPositionAsync(
        Document document,
        int position,
        int? selectionStart,
        int? selectionLength,
        CancellationToken ct)
    {
        var discovered = new List<DiscoveredAction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var filePath = document.FilePath ?? document.Name;
        var selectionSpan = CreateSelectionSpan(position, selectionStart, selectionLength);

        var providerDiagnostics = await GetProviderDiagnosticsForDocumentAsync(document, ct).ConfigureAwait(false);
        foreach (var diagnostic in providerDiagnostics)
        {
            ct.ThrowIfCancellationRequested();
            if (!diagnostic.Location.IsInSource)
            {
                continue;
            }

            var span = diagnostic.Location.SourceSpan;
            if (!span.Contains(position) || !IntersectsSelection(span, selectionStart, selectionLength))
            {
                continue;
            }

            var fixes = await CollectCodeFixActionsAsync(document, diagnostic, ct).ConfigureAwait(false);
            foreach (var fix in fixes)
            {
                var providerKey = BuildProviderCodeFixKey(fix.ProviderTypeName, diagnostic.Id, fix.Action.EquivalenceKey, fix.Action.Title);
                var category = GetCodeFixCategory(diagnostic);
                var key = string.Join('|', filePath, span.Start, span.Length, fix.Action.Title, category, providerKey);
                if (!seen.Add(key))
                {
                    continue;
                }

                discovered.Add(new DiscoveredAction(
                    fix.Action.Title,
                    category,
                    OriginRoslynatorCodeFix,
                    providerKey,
                    filePath,
                    span.Start,
                    span.Length,
                    CreateSourceLocation(diagnostic.Location),
                    diagnostic.Id,
                    NormalizeNullable(fix.Action.EquivalenceKey)));
            }
        }

        foreach (var action in await CollectCodeRefactoringActionsAsync(document, selectionSpan, ct).ConfigureAwait(false))
        {
            var span = selectionSpan;
            var providerKey = BuildProviderRefactoringKey(action.ProviderTypeName, action.Action.EquivalenceKey, action.Action.Title);
            var key = string.Join('|', filePath, span.Start, span.Length, action.Action.Title, RefactoringCategoryDefault, providerKey);
            if (!seen.Add(key))
            {
                continue;
            }

            discovered.Add(new DiscoveredAction(
                action.Action.Title,
                RefactoringCategoryDefault,
                OriginRoslynatorRefactoring,
                providerKey,
                filePath,
                span.Start,
                span.Length,
                await CreateSourceLocationFromSpanAsync(document, span, ct).ConfigureAwait(false),
                null,
                NormalizeNullable(action.Action.EquivalenceKey)));
        }

        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (semanticModel != null)
        {
            foreach (var diagnostic in semanticModel.GetDiagnostics()
                         .Where(static d => d.Location.IsInSource)
                         .OrderBy(static d => d.Location.GetLineSpan().Path, StringComparer.Ordinal)
                         .ThenBy(static d => d.Location.SourceSpan.Start)
                         .ThenBy(static d => d.Location.SourceSpan.Length)
                         .ThenBy(static d => d.Id, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if (!IsSupportedDiagnostic(diagnostic))
                {
                    continue;
                }

                var declaration = await TryGetUnusedLocalDeclarationAsync(document, diagnostic, ct).ConfigureAwait(false);
                if (declaration == null)
                {
                    continue;
                }

                var span = declaration.Span;
                if (!span.Contains(position) || !IntersectsSelection(span, selectionStart, selectionLength))
                {
                    continue;
                }

                var variableName = declaration.Declaration.Variables[0].Identifier.ValueText;
                var title = $"Remove unused local variable '{variableName}'";
                var providerKey = $"{SupportedFixOperation}:{diagnostic.Id}";
                var key = string.Join('|', filePath, span.Start, span.Length, title, SupportedFixCategory, providerKey);
                if (!seen.Add(key))
                {
                    continue;
                }

                discovered.Add(new DiscoveredAction(
                    title,
                    SupportedFixCategory,
                    OriginRoslynatorCodeFix,
                    providerKey,
                    filePath,
                    span.Start,
                    span.Length,
                    CreateSourceLocation(diagnostic.Location),
                    diagnostic.Id,
                    null));
            }
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var token = root?.FindToken(position);
        var localDeclaration = token?.Parent?.AncestorsAndSelf().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
        if (localDeclaration != null
            && !localDeclaration.IsConst
            && localDeclaration.Declaration.Variables.Count == 1
            && localDeclaration.Declaration.Type is not IdentifierNameSyntax { Identifier.ValueText: "var" })
        {
            var typeSpan = localDeclaration.Declaration.Type.Span;
            if (IntersectsSelection(typeSpan, selectionStart, selectionLength))
            {
                var key = string.Join('|', filePath, typeSpan.Start, typeSpan.Length, RefactoringOperationUseVar);
                if (seen.Add(key))
                {
                    discovered.Add(new DiscoveredAction(
                        "Use 'var' for local declaration",
                        "style",
                        OriginRoslynatorRefactoring,
                        RefactoringOperationUseVar,
                        filePath,
                        typeSpan.Start,
                        typeSpan.Length,
                        CreateSourceLocation(localDeclaration.GetLocation()),
                        null,
                        RefactoringOperationUseVar));
                }
            }
        }

        return discovered;
    }

    private async Task<FixOperation?> TryBuildActionOperationAsync(Solution solution, ActionExecutionIdentity identity, CancellationToken ct)
    {
        if (TryParseProviderCodeFixKey(identity.ProviderActionKey, out var codeFixKey))
        {
            return await TryBuildProviderCodeFixOperationAsync(solution, identity, codeFixKey, ct).ConfigureAwait(false);
        }

        if (TryParseProviderRefactoringKey(identity.ProviderActionKey, out var refactoringKey))
        {
            return await TryBuildProviderRefactoringOperationAsync(solution, identity, refactoringKey, ct).ConfigureAwait(false);
        }

        if (string.Equals(identity.ProviderActionKey, RefactoringOperationUseVar, StringComparison.Ordinal))
        {
            return await TryBuildUseVarOperationAsync(solution, identity, ct).ConfigureAwait(false);
        }

        if (identity.ProviderActionKey.StartsWith(SupportedFixOperation + ":", StringComparison.Ordinal))
        {
            var parsedFix = new ParsedFixId(identity.WorkspaceVersion,
                identity.DiagnosticId ?? string.Empty,
                identity.SpanStart,
                identity.SpanLength,
                identity.FilePath);
            return await TryBuildFixOperationAsync(solution, parsedFix, ct).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<FixOperation?> TryBuildUseVarOperationAsync(Solution solution, ActionExecutionIdentity identity, CancellationToken ct)
    {
        var document = solution.Projects
            .SelectMany(static project => project.Documents)
            .FirstOrDefault(d => MatchesByNormalizedPath(d.FilePath, identity.FilePath));
        if (document == null)
        {
            return null;
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root == null)
        {
            return null;
        }

        var typeSyntax = root.FindNode(new TextSpan(identity.SpanStart, identity.SpanLength)) as TypeSyntax;
        if (typeSyntax == null)
        {
            return null;
        }

        return new FixOperation(
            "Use 'var' for local declaration",
            async (currentSolution, cancellationToken) =>
            {
                var currentDocument = currentSolution.GetDocument(document.Id);
                if (currentDocument == null)
                {
                    return currentSolution;
                }

                var currentRoot = await currentDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                if (currentRoot == null)
                {
                    return currentSolution;
                }

                var currentTypeSyntax = currentRoot.FindNode(new TextSpan(identity.SpanStart, identity.SpanLength)) as TypeSyntax;
                if (currentTypeSyntax == null)
                {
                    return currentSolution;
                }

                var replacement = SyntaxFactory.IdentifierName("var").WithTriviaFrom(currentTypeSyntax);
                var updatedRoot = currentRoot.ReplaceNode(currentTypeSyntax, replacement);
                return currentSolution.WithDocumentSyntaxRoot(document.Id, updatedRoot);
            });
    }

    private async Task<FixOperation?> TryBuildProviderCodeFixOperationAsync(Solution solution,
        ActionExecutionIdentity identity,
        ProviderCodeFixKey key,
        CancellationToken ct)
    {
        var document = solution.Projects
            .SelectMany(static project => project.Documents)
            .FirstOrDefault(d => MatchesByNormalizedPath(d.FilePath, identity.FilePath));
        if (document == null)
        {
            return null;
        }

        var diagnostics = await GetProviderDiagnosticsForDocumentAsync(document, ct).ConfigureAwait(false);
        var matches = diagnostics
            .Where(d => d.Location.IsInSource
                        && string.Equals(d.Id, identity.DiagnosticId, StringComparison.Ordinal)
                        && d.Location.SourceSpan.Start == identity.SpanStart
                        && d.Location.SourceSpan.Length == identity.SpanLength)
            .OrderBy(static d => d.Id, StringComparer.Ordinal)
            .ToArray();

        foreach (var diagnostic in matches)
        {
            var actions = await CollectCodeFixActionsAsync(document, diagnostic, ct).ConfigureAwait(false);
            var selected = actions
                .Where(candidate => string.Equals(candidate.ProviderTypeName, key.ProviderTypeName, StringComparison.Ordinal))
                .Select(candidate => candidate.Action)
                .FirstOrDefault(action => MatchesProviderAction(identity, action, key.ActionTitle));
            if (selected == null)
            {
                continue;
            }

            return new FixOperation(
                selected.Title,
                async (currentSolution, cancellationToken) =>
                {
                    var currentDocument = FindDocument(currentSolution, identity.FilePath);
                    if (currentDocument == null)
                    {
                        return currentSolution;
                    }

                    var currentDiagnostics = await GetProviderDiagnosticsForDocumentAsync(currentDocument, cancellationToken).ConfigureAwait(false);
                    var currentDiagnostic = currentDiagnostics
                        .Where(d => d.Location.IsInSource
                                    && string.Equals(d.Id, identity.DiagnosticId, StringComparison.Ordinal)
                                    && d.Location.SourceSpan.Start == identity.SpanStart
                                    && d.Location.SourceSpan.Length == identity.SpanLength)
                        .OrderBy(static d => d.Id, StringComparer.Ordinal)
                        .FirstOrDefault();
                    if (currentDiagnostic == null)
                    {
                        return currentSolution;
                    }

                    var currentActions = await CollectCodeFixActionsAsync(currentDocument, currentDiagnostic, cancellationToken).ConfigureAwait(false);
                    var currentAction = currentActions
                        .Where(candidate => string.Equals(candidate.ProviderTypeName, key.ProviderTypeName, StringComparison.Ordinal))
                        .Select(candidate => candidate.Action)
                        .FirstOrDefault(action => MatchesProviderAction(identity, action, key.ActionTitle));

                    if (currentAction == null)
                    {
                        return currentSolution;
                    }

                    var applied = await TryApplyCodeActionToSolutionAsync(currentSolution, currentAction, cancellationToken).ConfigureAwait(false);
                    return applied ?? currentSolution;
                });
        }

        return null;
    }

    private async Task<FixOperation?> TryBuildProviderRefactoringOperationAsync(Solution solution,
        ActionExecutionIdentity identity,
        ProviderRefactoringKey key,
        CancellationToken ct)
    {
        var document = FindDocument(solution, identity.FilePath);
        if (document == null)
        {
            return null;
        }

        var span = new TextSpan(identity.SpanStart, identity.SpanLength);
        var actions = await CollectCodeRefactoringActionsAsync(document, span, ct).ConfigureAwait(false);
        var selected = actions
            .Where(candidate => string.Equals(candidate.ProviderTypeName, key.ProviderTypeName, StringComparison.Ordinal))
            .Select(candidate => candidate.Action)
            .FirstOrDefault(action => MatchesProviderAction(identity, action, key.ActionTitle));
        if (selected == null)
        {
            return null;
        }

        return new FixOperation(
            selected.Title,
            async (currentSolution, cancellationToken) =>
            {
                var currentDocument = FindDocument(currentSolution, identity.FilePath);
                if (currentDocument == null)
                {
                    return currentSolution;
                }

                var currentActions = await CollectCodeRefactoringActionsAsync(currentDocument, span, cancellationToken).ConfigureAwait(false);
                var currentAction = currentActions
                    .Where(candidate => string.Equals(candidate.ProviderTypeName, key.ProviderTypeName, StringComparison.Ordinal))
                    .Select(candidate => candidate.Action)
                    .FirstOrDefault(action => MatchesProviderAction(identity, action, key.ActionTitle));
                if (currentAction == null)
                {
                    return currentSolution;
                }

                var applied = await TryApplyCodeActionToSolutionAsync(currentSolution, currentAction, cancellationToken).ConfigureAwait(false);
                return applied ?? currentSolution;
            });
    }

    private static Document? FindDocument(Solution solution, string filePath)
        => solution.Projects.SelectMany(static project => project.Documents)
            .FirstOrDefault(d => MatchesByNormalizedPath(d.FilePath, filePath));

    private static bool MatchesProviderAction(ActionExecutionIdentity identity, CodeAction action, string actionTitle)
    {
        if (!string.IsNullOrWhiteSpace(identity.RefactoringId)
            && string.Equals(identity.RefactoringId, action.EquivalenceKey, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(action.Title, actionTitle, StringComparison.Ordinal);
    }

    private async Task<IReadOnlyList<Diagnostic>> GetProviderDiagnosticsForDocumentAsync(Document document, CancellationToken ct)
    {
        var diagnostics = new List<Diagnostic>();
        var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (semanticModel != null)
        {
            diagnostics.AddRange(semanticModel.GetDiagnostics()
                .Where(static d => d.Location.IsInSource));
        }

        var catalog = _providerCatalogService.Catalog;
        if (catalog.Error == null && !catalog.Analyzers.IsDefaultOrEmpty)
        {
            var compilation = await document.Project.GetCompilationAsync(ct).ConfigureAwait(false);
            var tree = await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false);
            if (compilation != null && tree != null)
            {
                var options = new CompilationWithAnalyzersOptions(
                    new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
                    onAnalyzerException: null,
                    concurrentAnalysis: false,
                    logAnalyzerExecutionTime: false,
                    reportSuppressedDiagnostics: false);
                var withAnalyzers = compilation.WithAnalyzers(catalog.Analyzers, options);
                var analyzerDiagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(ct).ConfigureAwait(false);
                diagnostics.AddRange(analyzerDiagnostics.Where(d => d.Location.IsInSource && ReferenceEquals(d.Location.SourceTree, tree)));
            }
        }

        return diagnostics
            .OrderBy(static d => d.Location.GetLineSpan().Path, StringComparer.Ordinal)
            .ThenBy(static d => d.Location.SourceSpan.Start)
            .ThenBy(static d => d.Location.SourceSpan.Length)
            .ThenBy(static d => d.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<ProviderCodeActionCandidate>> CollectCodeFixActionsAsync(Document document, Diagnostic diagnostic, CancellationToken ct)
    {
        var catalog = _providerCatalogService.Catalog;
        if (catalog.Error != null || catalog.CodeFixProviders.IsDefaultOrEmpty)
        {
            return Array.Empty<ProviderCodeActionCandidate>();
        }

        var candidates = new List<ProviderCodeActionCandidate>();
        foreach (var provider in catalog.CodeFixProviders.OrderBy(static p => p.GetType().FullName, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (!provider.FixableDiagnosticIds.Contains(diagnostic.Id, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var registered = new List<CodeAction>();
            var context = new CodeFixContext(
                document,
                diagnostic,
                (action, _) =>
                {
                    if (action != null)
                    {
                        registered.Add(action);
                    }
                },
                ct);

            try
            {
                await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Code fix provider failed: {ProviderType}", provider.GetType().FullName);
                continue;
            }

            foreach (var action in registered.OrderBy(static a => a.Title, StringComparer.Ordinal)
                         .ThenBy(static a => a.EquivalenceKey ?? string.Empty, StringComparer.Ordinal))
            {
                candidates.Add(new ProviderCodeActionCandidate(provider.GetType().FullName ?? provider.GetType().Name, action));
            }
        }

        return candidates;
    }

    private async Task<IReadOnlyList<ProviderCodeActionCandidate>> CollectCodeRefactoringActionsAsync(Document document, TextSpan selectionSpan, CancellationToken ct)
    {
        var catalog = _providerCatalogService.Catalog;
        if (catalog.Error != null || catalog.RefactoringProviders.IsDefaultOrEmpty)
        {
            return Array.Empty<ProviderCodeActionCandidate>();
        }

        var candidates = new List<ProviderCodeActionCandidate>();
        foreach (var provider in catalog.RefactoringProviders.OrderBy(static p => p.GetType().FullName, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var registered = new List<CodeAction>();
            var context = new CodeRefactoringContext(
                document,
                selectionSpan,
                action =>
                {
                    if (action != null)
                    {
                        registered.Add(action);
                    }
                },
                ct);
            try
            {
                await provider.ComputeRefactoringsAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Refactoring provider failed: {ProviderType}", provider.GetType().FullName);
                continue;
            }

            foreach (var action in registered.OrderBy(static a => a.Title, StringComparer.Ordinal)
                         .ThenBy(static a => a.EquivalenceKey ?? string.Empty, StringComparer.Ordinal))
            {
                candidates.Add(new ProviderCodeActionCandidate(provider.GetType().FullName ?? provider.GetType().Name, action));
            }
        }

        return candidates;
    }

    private static TextSpan CreateSelectionSpan(int position, int? selectionStart, int? selectionLength)
    {
        if (selectionStart.HasValue && selectionLength.HasValue)
        {
            return new TextSpan(selectionStart.Value, selectionLength.Value);
        }

        return new TextSpan(position, 0);
    }

    private static string GetCodeFixCategory(Diagnostic diagnostic)
        => string.IsNullOrWhiteSpace(diagnostic.Descriptor.Category)
            ? SupportedFixCategory
            : diagnostic.Descriptor.Category.Trim().ToLowerInvariant();

    private static string BuildProviderCodeFixKey(string providerType, string diagnosticId, string? equivalenceKey, string title)
        => string.Join('|', "cf", EncodeKey(providerType), EncodeKey(diagnosticId), EncodeKey(equivalenceKey), EncodeKey(title));

    private static string BuildProviderRefactoringKey(string providerType, string? equivalenceKey, string title)
        => string.Join('|', "rf", EncodeKey(providerType), EncodeKey(equivalenceKey), EncodeKey(title));

    private static bool TryParseProviderCodeFixKey(string key, out ProviderCodeFixKey parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var parts = key.Split('|');
        if (parts.Length != 5 || !string.Equals(parts[0], "cf", StringComparison.Ordinal))
        {
            return false;
        }

        var providerType = DecodeKey(parts[1]);
        var diagnosticId = DecodeKey(parts[2]);
        if (string.IsNullOrWhiteSpace(providerType) || string.IsNullOrWhiteSpace(diagnosticId))
        {
            return false;
        }

        parsed = new ProviderCodeFixKey(providerType, diagnosticId, NormalizeNullable(DecodeKey(parts[3])), DecodeKey(parts[4]));
        return true;
    }

    private static bool TryParseProviderRefactoringKey(string key, out ProviderRefactoringKey parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var parts = key.Split('|');
        if (parts.Length != 4 || !string.Equals(parts[0], "rf", StringComparison.Ordinal))
        {
            return false;
        }

        var providerType = DecodeKey(parts[1]);
        if (string.IsNullOrWhiteSpace(providerType))
        {
            return false;
        }

        parsed = new ProviderRefactoringKey(providerType, NormalizeNullable(DecodeKey(parts[2])), DecodeKey(parts[3]));
        return true;
    }

    private static string EncodeKey(string? value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

    private static string DecodeKey(string encoded)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static async Task<SourceLocation> CreateSourceLocationFromSpanAsync(Document document, TextSpan span, CancellationToken ct)
    {
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        var line = text.Lines.GetLineFromPosition(span.Start);
        return new SourceLocation(document.FilePath ?? document.Name, line.LineNumber + 1, span.Start - line.Start + 1);
    }

    private async Task<Solution?> TryApplyCodeActionToSolutionAsync(Solution currentSolution, CodeAction action, CancellationToken ct)
    {
        try
        {
            var operations = await action.GetOperationsAsync(ct).ConfigureAwait(false);
            var applyOperation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
            return applyOperation?.ChangedSolution;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Failed to apply provider action {ActionTitle}", action.Title);
            return null;
        }
    }

    private void LogActionPipelineFlow(
        string operation,
        string actionOrigin,
        string actionType,
        string policyDecision,
        long startedAt,
        string resultCode,
        int affectedDocumentCount)
    {
        var duration = Stopwatch.GetElapsedTime(startedAt);
        _logger.LogInformation(
            "{EventName} operation={Operation} actionOrigin={ActionOrigin} actionType={ActionType} policyDecision={PolicyDecision} durationMs={DurationMs} resultCode={ResultCode} affectedDocumentCount={AffectedDocumentCount}",
            RefactoringActionPipelineFlowLog,
            operation,
            actionOrigin,
            actionType,
            policyDecision,
            duration.TotalMilliseconds,
            resultCode,
            affectedDocumentCount);
    }

    private static bool IntersectsSelection(TextSpan span, int? selectionStart, int? selectionLength)
    {
        if (!selectionStart.HasValue || !selectionLength.HasValue)
        {
            return true;
        }

        var selection = new TextSpan(selectionStart.Value, selectionLength.Value);
        return selection.OverlapsWith(span) || selection.Contains(span.Start) || span.Contains(selection.Start);
    }

    private static PreviewCodeFixResult CreatePreviewError(string code, string message)
        => CreatePreviewError(CreateError(code, message, ("operation", "preview_code_fix")));

    private static PreviewCodeFixResult CreatePreviewError(ErrorInfo error)
        => new(string.Empty, string.Empty, Array.Empty<ChangedFilePreview>(), error);

    private static ApplyCodeFixResult CreateApplyError(string fixId, string code, string message)
        => new(fixId,
            0,
            Array.Empty<string>(),
            CreateError(code, message, ("fixId", fixId), ("operation", "apply_code_fix")));

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private readonly record struct ProviderCodeFixKey(string ProviderTypeName, string DiagnosticId, string? EquivalenceKey, string ActionTitle);

    private readonly record struct ProviderRefactoringKey(string ProviderTypeName, string? EquivalenceKey, string ActionTitle);

    private sealed record ProviderCodeActionCandidate(string ProviderTypeName, CodeAction Action);

    private sealed record DiscoveredAction(
        string Title,
        string Category,
        string Origin,
        string ProviderActionKey,
        string FilePath,
        int SpanStart,
        int SpanLength,
        SourceLocation Location,
        string? DiagnosticId,
        string? RefactoringId);

    private sealed record ActionExecutionIdentity(
        int WorkspaceVersion,
        string PolicyProfile,
        string Origin,
        string Category,
        string ProviderActionKey,
        string FilePath,
        int SpanStart,
        int SpanLength,
        string? DiagnosticId,
        string? RefactoringId,
        SourceLocation Location)
    {
        public DiscoveredAction ToDiscoveredAction()
            => new(
                string.Empty,
                Category,
                Origin,
                ProviderActionKey,
                FilePath,
                SpanStart,
                SpanLength,
                Location,
                DiagnosticId,
                RefactoringId);
    }

    private sealed class ActionIdentityService
    {
        public string Create(int workspaceVersion, string policyProfile, DiscoveredAction action)
            => string.Join('|',
                "v1",
                workspaceVersion,
                Encode(policyProfile),
                Encode(action.Origin),
                Encode(action.Category),
                Encode(action.ProviderActionKey),
                action.SpanStart,
                action.SpanLength,
                Encode(action.FilePath),
                Encode(action.DiagnosticId),
                Encode(action.RefactoringId),
                action.Location.Line,
                action.Location.Column);

        public ActionExecutionIdentity? Parse(string actionId)
        {
            if (string.IsNullOrWhiteSpace(actionId))
            {
                return null;
            }

            var parts = actionId.Split('|');
            if (parts.Length != 13 || !string.Equals(parts[0], "v1", StringComparison.Ordinal))
            {
                return null;
            }

            if (!int.TryParse(parts[1], out var workspaceVersion)
                || !int.TryParse(parts[6], out var spanStart)
                || !int.TryParse(parts[7], out var spanLength)
                || !int.TryParse(parts[11], out var line)
                || !int.TryParse(parts[12], out var column))
            {
                return null;
            }

            var policyProfile = Decode(parts[2]);
            var origin = Decode(parts[3]);
            var category = Decode(parts[4]);
            var providerKey = Decode(parts[5]);
            var filePath = Decode(parts[8]);
            if (string.IsNullOrWhiteSpace(origin)
                || string.IsNullOrWhiteSpace(category)
                || string.IsNullOrWhiteSpace(providerKey)
                || string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            return new ActionExecutionIdentity(
                workspaceVersion,
                string.IsNullOrWhiteSpace(policyProfile) ? PolicyProfileDefault : policyProfile,
                origin,
                category,
                providerKey,
                filePath,
                spanStart,
                spanLength,
                NormalizeNullable(Decode(parts[9])),
                NormalizeNullable(Decode(parts[10])),
                new SourceLocation(filePath, line, column));
        }

        private static string Encode(string? value)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

        private static string Decode(string encoded)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            }
            catch (FormatException)
            {
                return string.Empty;
            }
        }

        private static string? NormalizeNullable(string value)
            => string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed class RefactoringPolicyService
    {
        public PolicyAssessment Evaluate(DiscoveredAction action, string policyProfile)
        {
            var profile = string.IsNullOrWhiteSpace(policyProfile)
                ? PolicyProfileDefault
                : policyProfile.Trim();

            if (!string.Equals(profile, PolicyProfileDefault, StringComparison.OrdinalIgnoreCase))
            {
                return new PolicyAssessment(
                    "block",
                    "blocked",
                    "unknown_profile",
                    $"Policy profile '{profile}' is not supported.");
            }

            if (string.Equals(action.ProviderActionKey, RefactoringOperationUseVar, StringComparison.Ordinal))
            {
                return new PolicyAssessment(
                    "review_required",
                    "review_required",
                    "manual_review_required",
                    "This refactoring requires manual review before apply.");
            }

            if (string.Equals(action.Origin, OriginRoslynatorCodeFix, StringComparison.Ordinal)
                && string.Equals(action.Category, SupportedFixCategory, StringComparison.Ordinal)
                && action.DiagnosticId != null
                && SupportedFixDiagnosticIds.Contains(action.DiagnosticId))
            {
                return new PolicyAssessment(
                    "allow",
                    "safe",
                    "allowlisted",
                    "Action is allowlisted in the default policy profile.");
            }

            return new PolicyAssessment(
                "block",
                "blocked",
                "not_allowlisted",
                "Action is not allowlisted in the default policy profile.");
        }
    }

    private sealed record PolicyAssessment(string Decision, string RiskLevel, string ReasonCode, string ReasonMessage);

    private sealed record ParsedFixId(int WorkspaceVersion, string DiagnosticId, int SpanStart, int SpanLength, string FilePath);

    private sealed record FixOperation(string Title, Func<Solution, CancellationToken, Task<Solution>> ApplyAsync);

    private static class SymbolIdentity
    {
        private static readonly MethodInfo s_createString;
        private static readonly MethodInfo s_resolveString;
        private static readonly PropertyInfo s_resolutionSymbol;

        static SymbolIdentity()
        {
            var assembly = typeof(SymbolFinder).Assembly;
            var symbolKeyType = assembly.GetType("Microsoft.CodeAnalysis.SymbolKey", throwOnError: true)!;
            var resolutionType = assembly.GetType("Microsoft.CodeAnalysis.SymbolKeyResolution", throwOnError: true)!;

            s_createString = symbolKeyType.GetMethod("CreateString", BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(ISymbol), typeof(CancellationToken) },
                modifiers: null)
                ?? throw new InvalidOperationException("Unable to locate SymbolKey.CreateString");

            s_resolveString = symbolKeyType.GetMethod("ResolveString", BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string), typeof(Compilation), typeof(bool), typeof(CancellationToken) },
                modifiers: null)
                ?? throw new InvalidOperationException("Unable to locate SymbolKey.ResolveString");

            s_resolutionSymbol = resolutionType.GetProperty("Symbol", BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Unable to locate SymbolKeyResolution.Symbol");
        }

        public static string CreateId(ISymbol symbol)
        {
            var resolved = symbol.OriginalDefinition ?? symbol;
            var result = (string?)s_createString.Invoke(null, new object?[] { resolved, CancellationToken.None });
            if (!string.IsNullOrEmpty(result))
            {
                return result;
            }

            return resolved.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        public static ISymbol? Resolve(string identifier, Compilation compilation, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                return null;
            }

            var resolution = s_resolveString.Invoke(null, new object?[] { identifier, compilation, true, ct });
            if (resolution == null)
            {
                return null;
            }

            return (ISymbol?)s_resolutionSymbol.GetValue(resolution);
        }
    }
}
