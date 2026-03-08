using RoslynMcp.Core;
using RoslynMcp.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynMcp.Infrastructure.Navigation;

internal sealed class ReferenceSearchService : IReferenceSearchService
{
    public bool IsValidScope(string scope)
        => string.Equals(scope, ReferenceScopes.Document, StringComparison.Ordinal) ||
           string.Equals(scope, ReferenceScopes.Project, StringComparison.Ordinal) ||
           string.Equals(scope, ReferenceScopes.Solution, StringComparison.Ordinal);

    public ErrorInfo? TryValidateDocumentPath(string path, Solution solution)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return NavigationErrorFactory.CreateError(ErrorCodes.InvalidPath,
                "path must be provided for document scope.",
                ("parameter", "path"),
                ("operation", "find-references-scoped"));
        }

        if (solution.Projects
            .SelectMany(static project => project.Documents)
            .Any(document => string.Equals(document.FilePath, path, StringComparison.OrdinalIgnoreCase) ||
                             document.FilePath.MatchesByNormalizedPath(path)))
        {
            return null;
        }

        return NavigationErrorFactory.CreateError(ErrorCodes.InvalidPath,
            $"Document path '{path}' is not part of the selected solution.",
            ("path", path),
            ("operation", "find-references-scoped"));
    }

    public async Task<IReadOnlyList<SourceLocation>> FindReferencesAsync(ISymbol symbol, Solution solution, CancellationToken ct)
    {
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        return CollectOrderedLocations(references, static (_, _) => true, ct);
    }

    public async Task<IReadOnlyList<SourceLocation>> FindReferencesScopedAsync(
        ISymbol symbol,
        Solution solution,
        string scope,
        string? path,
        Project? ownerProject,
        CancellationToken ct)
    {
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct).ConfigureAwait(false);
        return CollectOrderedLocations(
            references,
            (referenceLocation, document) => IsReferenceInScope(scope, path, ownerProject, document),
            ct);
    }

    public async Task<IReadOnlyList<SymbolDescriptor>> FindImplementationsAsync(ISymbol symbol, Solution solution, CancellationToken ct)
    {
        var implementations = await PolymorphicImplementationDiscovery.FindImplementationSymbolsAsync(symbol, solution, ct).ConfigureAwait(false);

        var uniqueDescriptors = new Dictionary<string, SymbolDescriptor>(StringComparer.Ordinal);
        foreach (var implementation in implementations)
        {
            var descriptor = implementation.ToSymbolDescriptor();
            uniqueDescriptors[descriptor.SymbolId] = descriptor;
        }

        return uniqueDescriptors.Values
            .OrderBy(static descriptor => descriptor, SymbolDescriptorComparer.Instance)
            .ToArray();
    }

    private static IReadOnlyList<SourceLocation> CollectOrderedLocations(
        IEnumerable<ReferencedSymbol> references,
        Func<ReferenceLocation, Document?, bool> include,
        CancellationToken ct)
    {
        var uniqueLocations = new HashSet<string>(StringComparer.Ordinal);
        var locations = new List<SourceLocation>();

        foreach (var reference in references)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var location in reference.Locations)
            {
                if (!location.Location.IsInSource || !include(location, location.Document))
                {
                    continue;
                }

                var sourceLocation = location.Location.ToSourceLocation();
                if (uniqueLocations.Add(sourceLocation.GetLocationKey()))
                {
                    locations.Add(sourceLocation);
                }
            }
        }

        return locations.OrderBy(static loc => loc, SourceLocationComparer.Instance).ToArray();
    }

    private static bool IsReferenceInScope(string scope, string? path, Project? ownerProject, Document? document)
    {
        if (document == null)
        {
            return false;
        }

        if (string.Equals(scope, ReferenceScopes.Solution, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(scope, ReferenceScopes.Project, StringComparison.Ordinal))
        {
            return ownerProject != null && document.Project.Id == ownerProject.Id;
        }

        return string.Equals(scope, ReferenceScopes.Document, StringComparison.Ordinal) &&
               !string.IsNullOrWhiteSpace(path) &&
                (string.Equals(document.FilePath, path, StringComparison.OrdinalIgnoreCase) ||
                 document.FilePath.MatchesByNormalizedPath(path));
    }
}
