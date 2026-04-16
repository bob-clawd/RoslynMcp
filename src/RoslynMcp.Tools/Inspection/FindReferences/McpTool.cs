using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.FindReferences;

public sealed record ReferenceContext(
    string FilePath,
    string? ContainingTypeSymbolId = null);

public sealed record Result(
    IReadOnlyList<ReferenceContext> References,
    ErrorInfo? Error = null)
{
    public static Result AsError(string message, IReadOnlyDictionary<string, string>? details = null)
        => new([], new ErrorInfo(message, details));
}

[McpServerToolType]
public sealed class McpTool(WorkspaceManager workspaceManager, SolutionManager solutionManager, SymbolManager symbolManager) : Tool
{
    [McpServerTool(Name = "find_references", Title = "Find References", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to see which files and containing types reference a symbol.")]
    public async Task<Result> Execute(
        CancellationToken cancellationToken,
        [Description("The stable symbol ID of a type or member.")]
        string? symbolId = null)
    {
        if (solutionManager.Solution is not { } solution)
            return Result.AsError("load solution first");

        if (string.IsNullOrWhiteSpace(symbolId))
            return Result.AsError("symbolId is required");

        if (symbolManager.ToSymbol(symbolId) is not { } symbol)
            return Result.AsError("symbol not found");

        var references = await FindContexts(symbol, solution, cancellationToken).ConfigureAwait(false);
        return new Result(ToReferences(references));
    }

    private async Task<List<ResolvedReference>> FindContexts(ISymbol symbol, Solution solution, CancellationToken cancellationToken)
    {
        var referencedSymbols = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken: cancellationToken).ConfigureAwait(false);
        var semanticModels = new Dictionary<DocumentId, Task<SemanticModel?>>();
        var references = new List<ResolvedReference>();

        foreach (var referencedSymbol in referencedSymbols)
        {
            foreach (var location in referencedSymbol.Locations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!location.Location.IsInSource)
                    continue;

                if (location.Document.FilePath is not { } filePath || string.IsNullOrWhiteSpace(filePath))
                    continue;

                var relativePath = workspaceManager.ToRelativePathIfPossible(filePath) ?? filePath;
                var containingType = await FindContainingType(location, semanticModels, cancellationToken).ConfigureAwait(false);

                references.Add(new ResolvedReference(
                    relativePath,
                    containingType?.SymbolId,
                    containingType?.DisplayName));
            }
        }

        return SelectReferences(references);
    }

    private async Task<ResolvedContainingType?> FindContainingType(
        ReferenceLocation location,
        Dictionary<DocumentId, Task<SemanticModel?>> semanticModels,
        CancellationToken cancellationToken)
    {
        if (!semanticModels.TryGetValue(location.Document.Id, out var semanticModelTask))
        {
            semanticModelTask = location.Document.GetSemanticModelAsync(cancellationToken);
            semanticModels.Add(location.Document.Id, semanticModelTask);
        }

        if (await semanticModelTask.ConfigureAwait(false) is not { } semanticModel)
            return null;

        var containingType = semanticModel.GetEnclosingSymbol(location.Location.SourceSpan.Start, cancellationToken) switch
        {
            INamedTypeSymbol type => type,
            { ContainingType: { } type } => type,
            _ => null
        };

        if (containingType is null)
            return null;

        containingType = containingType.OriginalDefinition;

        return new ResolvedContainingType(
            symbolManager.ToId(containingType),
            containingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }

    private static List<ResolvedReference> SelectReferences(List<ResolvedReference> references)
    {
        var selected = references
            .DistinctBy(reference => (reference.FilePath, reference.ContainingTypeSymbolId))
            .ToList();

        if (selected.Any(reference => reference.FilePath.IsHandwritten()))
            selected.RemoveAll(reference => !reference.FilePath.IsHandwritten());

        return selected;
    }

    private static IReadOnlyList<ReferenceContext> ToReferences(List<ResolvedReference> references)
        => references
            .OrderBy(reference => reference.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.ContainingTypeDisplayName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(reference => reference.ContainingTypeSymbolId ?? string.Empty, StringComparer.Ordinal)
            .Select(reference => new ReferenceContext(reference.FilePath, reference.ContainingTypeSymbolId))
            .ToList();

    private sealed record ResolvedReference(
        string FilePath,
        string? ContainingTypeSymbolId,
        string? ContainingTypeDisplayName);

    private sealed record ResolvedContainingType(
        string SymbolId,
        string DisplayName);
}
