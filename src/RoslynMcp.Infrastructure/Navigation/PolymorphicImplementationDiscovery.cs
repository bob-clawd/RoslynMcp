using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using System.Collections.Immutable;

namespace RoslynMcp.Infrastructure.Navigation;

/// <summary>
/// Finds polymorphic implementations: derived types that implement or override a given symbol.
/// Handles interfaces, abstract methods, and virtual members.
/// </summary>
internal static class PolymorphicImplementationDiscovery
{
    public static async Task<IReadOnlyList<ISymbol>> FindImplementationSymbolsAsync(ISymbol symbol, Solution solution, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(solution);

        var projects = solution.Projects.ToImmutableHashSet();
        var unique = new Dictionary<string, ISymbol>(StringComparer.Ordinal);

        async Task addAsync(IEnumerable<ISymbol> symbols)
        {
            foreach (var candidate in symbols)
            {
                ct.ThrowIfCancellationRequested();

                var normalized = NormalizeResultSymbol(candidate);
                var key = normalized.CreateId();
                unique[key] = normalized;
            }
        }

        var searchRoot = NormalizeSearchRoot(symbol);

        var implementations = await SymbolFinder.FindImplementationsAsync(searchRoot, solution, projects, ct).ConfigureAwait(false);
        await addAsync(implementations).ConfigureAwait(false);

        if (searchRoot is IMethodSymbol or IPropertySymbol or IEventSymbol)
        {
            var overrides = await SymbolFinder.FindOverridesAsync(searchRoot, solution, projects, ct).ConfigureAwait(false);
            await addAsync(overrides).ConfigureAwait(false);
        }

        return unique.Values
            .OrderBy(static candidate => candidate.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), StringComparer.Ordinal)
            .ToArray();
    }

    private static ISymbol NormalizeSearchRoot(ISymbol symbol)
    {
        if (symbol is IMethodSymbol method)
        {
            if (method.ExplicitInterfaceImplementations.Length > 0)
            {
                return method.ExplicitInterfaceImplementations[0].OriginalDefinition;
            }

            if (method.IsOverride && method.OverriddenMethod != null)
            {
                return method.OverriddenMethod.OriginalDefinition;
            }
        }

        if (symbol is IPropertySymbol property)
        {
            if (property.ExplicitInterfaceImplementations.Length > 0)
            {
                return property.ExplicitInterfaceImplementations[0].OriginalDefinition;
            }

            if (property.IsOverride && property.OverriddenProperty != null)
            {
                return property.OverriddenProperty.OriginalDefinition;
            }
        }

        if (symbol is IEventSymbol eventSymbol)
        {
            if (eventSymbol.ExplicitInterfaceImplementations.Length > 0)
            {
                return eventSymbol.ExplicitInterfaceImplementations[0].OriginalDefinition;
            }

            if (eventSymbol.IsOverride && eventSymbol.OverriddenEvent != null)
            {
                return eventSymbol.OverriddenEvent.OriginalDefinition;
            }
        }

        return symbol.OriginalDefinition ?? symbol;
    }

    private static ISymbol NormalizeResultSymbol(ISymbol symbol)
    {
        if (symbol is IMethodSymbol method)
        {
            return method.ConstructedFrom ?? method.OriginalDefinition ?? method;
        }

        if (symbol is IPropertySymbol property)
        {
            return property.OriginalDefinition ?? property;
        }

        if (symbol is IEventSymbol eventSymbol)
        {
            return eventSymbol.OriginalDefinition ?? eventSymbol;
        }

        if (symbol is INamedTypeSymbol namedType)
        {
            return namedType.OriginalDefinition ?? namedType;
        }

        return symbol.OriginalDefinition ?? symbol;
    }
}
