using Microsoft.CodeAnalysis;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Extensions;

internal static class SymbolExtensions
{
    internal static IEnumerable<INamedTypeSymbol> GetTypes(this INamespaceSymbol namespaceSymbol)
    {
        return namespaceSymbol
            .GetAllTypes()
            .Where(symbol => symbol.Locations.Any(location => location.IsInSource));
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(this INamespaceSymbol namespaceSymbol) => namespaceSymbol
        .GetTypeMembers()
        .Concat(namespaceSymbol.GetNamespaceMembers().SelectMany(GetAllTypes));

    internal static string ToTypeKind(this ITypeSymbol symbol)
    {
        return symbol.IsRecord ? "record" : nameof(symbol.TypeKind).ToLower();
    }

    internal static IReadOnlyList<string> MembersPreview(this ITypeSymbol symbol, SymbolManager symbolManager, WorkspaceManager workspaceManager)
    {
        return symbol.GetMembers()
            .Where(m => m.DeclaredAccessibility > Accessibility.Private)
            .Select(m => MemberSymbol.From(m, symbolManager, workspaceManager))
            .Where(m => m.Kind != null)
            .Take(3)
            .OrderBy(m => m.Kind, StringComparer.Ordinal)
            .ThenBy(m => m.DisplayName, StringComparer.Ordinal)
            .Select(m => m.Text)
            .ToArray();
    }
    
    internal static int MembersCount(this ITypeSymbol symbol)
    {
        return symbol.GetMembers()
            .Where(m => m.DeclaredAccessibility > Accessibility.Private)
            .Count(m => m.ToMemberKind() is not null);
    }

    internal static string? ToMemberKind(this ISymbol symbol)
    {
        return symbol switch
        {
            IPropertySymbol => "property",
            IFieldSymbol { IsImplicitlyDeclared: false } => "field",
            IEventSymbol => "event",
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => "ctor",
            IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.UserDefinedOperator or MethodKind.Conversion or MethodKind.ReducedExtension or MethodKind.DelegateInvoke } => "method",
            _ => null
        };
    }

    internal static string GetLocation(this ISymbol symbol, WorkspaceManager workspaceManager)
    {
        return string.Join(", ", symbol.Locations
            .Select(l => l.GetLineSpan())
            .Where(pos => pos.IsValid)
            .Select(pos => $"{workspaceManager.ToRelativePathIfPossible(pos.Path)}:{pos.StartLinePosition.Line + 1}"));
    }

    internal static string ToLightweightMemberSignature(this ISymbol symbol) => symbol switch
    {
        IPropertySymbol property => $"{property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {property.Name} {{ {(property.GetMethod is null ? string.Empty : "get; ")}{(property.SetMethod is null ? string.Empty : property.SetMethod.IsInitOnly ? "init;" : "set;")} }}".Trim(),
        IFieldSymbol field => $"{field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {field.Name}",
        IEventSymbol @event => $"event {@event.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {@event.Name}",
        IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } ctor => $"{ctor.ContainingType.Name}({string.Join(", ", ctor.Parameters.Select(ToText))})",
        IMethodSymbol method => $"{method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {method.Name}({string.Join(", ", method.Parameters.Select(ToText))})",
        _ => symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
    };

    private static string ToText(this IParameterSymbol parameter)
    {
        var modifier = parameter switch
        {
            { IsParams: true } => "params ",
            { RefKind: RefKind.Ref } => "ref ",
            { RefKind: RefKind.Out } => "out ",
            { RefKind: RefKind.In } => "in ",
            _ => string.Empty
        };

        return $"{modifier}{parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)} {parameter.Name}";
    }

    internal static string ToText(this Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Protected => "protected",
        Accessibility.Private => "private",
        Accessibility.ProtectedAndInternal => "private_protected",
        Accessibility.ProtectedOrInternal => "protected_internal",
        _ => nameof(accessibility).ToLower()
    };
}
