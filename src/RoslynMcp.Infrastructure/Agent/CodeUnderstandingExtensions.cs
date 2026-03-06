using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Navigation;

namespace RoslynMcp.Infrastructure.Agent;

public static partial class CodeUnderstandingExtensions
{
    public static IEnumerable<INamedTypeSymbol> EnumerateTypes(this INamespaceSymbol root)
    {
        var stack = new Stack<INamespaceOrTypeSymbol>();
        foreach (var member in root.GetMembers().OrderBy(static m => m.Name, StringComparer.Ordinal))
        {
            stack.Push(member);
        }

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current is INamedTypeSymbol namedType)
            {
                yield return namedType;
                foreach (var nested in namedType.GetTypeMembers().OrderByDescending(static m => m.Name, StringComparer.Ordinal))
                {
                    stack.Push(nested);
                }

                continue;
            }

            if (current is INamespaceSymbol ns)
            {
                foreach (var member in ns.GetMembers().OrderByDescending(static m => m.Name, StringComparer.Ordinal))
                {
                    stack.Push(member);
                }
            }
        }
    }

    public static ImmutableArray<ISymbol> CollectMembersWithInheritance(this INamedTypeSymbol type)
    {
        var builder = ImmutableArray.CreateBuilder<ISymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        static IEnumerable<INamedTypeSymbol> Traverse(INamedTypeSymbol current)
        {
            yield return current;

            var baseType = current.BaseType;
            while (baseType != null)
            {
                yield return baseType;
                baseType = baseType.BaseType;
            }

            foreach (var iface in current.AllInterfaces.OrderBy(static i => i.ToDisplayString(), StringComparer.Ordinal))
            {
                yield return iface;
            }
        }

        foreach (var declaringType in Traverse(type))
        {
            foreach (var member in declaringType.GetMembers())
            {
                var kind = member.ToMemberKind();
                if (kind == null)
                {
                    continue;
                }

                var key = SymbolIdentity.CreateId(member);
                if (seen.Add(key))
                {
                    builder.Add(member);
                }
            }
        }

        return builder.ToImmutable();
    }

    public static MemberListEntry? ToMemberEntry(
        this ISymbol member,
        string? normalizedKind,
        string? normalizedAccessibility,
        string? normalizedBinding)
    {
        var memberKind = member.ToMemberKind();
        if (memberKind == null)
        {
            return null;
        }

        if (normalizedKind != null && !string.Equals(memberKind, normalizedKind, StringComparison.Ordinal))
        {
            return null;
        }

        var accessibility = member.DeclaredAccessibility.NormalizeAccessibility();
        if (normalizedAccessibility != null && !string.Equals(accessibility, normalizedAccessibility, StringComparison.Ordinal))
        {
            return null;
        }

        if (normalizedBinding != null)
        {
            var isStatic = member.IsStatic;
            if ((string.Equals(normalizedBinding, "static", StringComparison.Ordinal) && !isStatic)
                || (string.Equals(normalizedBinding, "instance", StringComparison.Ordinal) && isStatic))
            {
                return null;
            }
        }

        var (filePath, line, column) = member.GetDeclarationPosition();
        return new MemberListEntry(
            member.Kind == SymbolKind.Method && member is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
                ? constructor.ContainingType.Name
                : member.Name,
            SymbolIdentity.CreateId(member),
            memberKind,
            member.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.MinimallyQualifiedFormat),
            filePath,
            line,
            column,
            accessibility,
            member.IsStatic);
    }

    public static string? ToTypeKind(this INamedTypeSymbol type)
    {
        if (type.IsRecord)
        {
            return "record";
        }

        return type.TypeKind switch
        {
            TypeKind.Class => "class",
            TypeKind.Interface => "interface",
            TypeKind.Enum => "enum",
            TypeKind.Struct => "struct",
            _ => null
        };
    }

    public static string? ToMemberKind(this ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => "ctor",
            IMethodSymbol method when method.MethodKind == MethodKind.Ordinary || method.MethodKind == MethodKind.UserDefinedOperator
                || method.MethodKind == MethodKind.Conversion || method.MethodKind == MethodKind.ReducedExtension
                || method.MethodKind == MethodKind.DelegateInvoke => "method",
            IPropertySymbol => "property",
            IFieldSymbol field when !field.IsImplicitlyDeclared => "field",
            IEventSymbol => "event",
            _ => null
        };
    }

    public static bool IsPartial(this INamedTypeSymbol symbol)
    {
        foreach (var syntaxReference in symbol.DeclaringSyntaxReferences)
        {
            var syntax = syntaxReference.GetSyntax();
            if (syntax is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax typeDeclaration
                && typeDeclaration.Modifiers.Any(modifier => modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)))
            {
                return true;
            }
        }

        return false;
    }

    public static (string FilePath, int? Line, int? Column) GetDeclarationPosition(this ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(static l => l.IsInSource);
        if (location == null)
        {
            return (string.Empty, null, null);
        }

        var span = location.GetLineSpan();
        var start = span.StartLinePosition;
        return (span.Path ?? string.Empty, start.Line + 1, start.Character + 1);
    }

    public static (string FilePath, int? StartLine, int? StartColumn, int? EndLine, int? EndColumn) GetSourceSpan(this ISymbol? symbol)
    {
        var location = symbol?.Locations.FirstOrDefault(static l => l.IsInSource);
        if (location == null)
        {
            return (string.Empty, null, null, null, null);
        }

        var span = location.GetLineSpan();
        var start = span.StartLinePosition;
        var end = span.EndLinePosition;
        return (span.Path ?? string.Empty, start.Line + 1, start.Character + 1, end.Line + 1, end.Character + 1);
    }

    public static string NormalizeAccessibility(this Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.Private => "private",
            Accessibility.ProtectedAndInternal => "private_protected",
            Accessibility.ProtectedOrInternal => "protected_internal",
            _ => "not_applicable"
        };
    }

    public static string NormalizeNamespace(this INamespaceSymbol? ns)
    {
        if (ns == null || ns.IsGlobalNamespace)
        {
            return string.Empty;
        }

        return ns.ToDisplayString();
    }

    public static string NormalizeQualifiedName(this string value)
        => value.Trim().Replace("global::", string.Empty, StringComparison.Ordinal);

    public static bool LooksLikeShortNameQuery(this string normalizedQualifiedName)
        => normalizedQualifiedName.IndexOf('.') < 0;

    public static bool MatchesQualifiedName(this ISymbol symbol, string normalizedQualifiedName)
    {
        var full = symbol.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat).NormalizeQualifiedName();
        if (string.Equals(full, normalizedQualifiedName, StringComparison.Ordinal))
        {
            return true;
        }

        var csharpError = symbol.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.CSharpErrorMessageFormat).NormalizeQualifiedName();
        return string.Equals(csharpError, normalizedQualifiedName, StringComparison.Ordinal);
    }

    public static ResolvedSymbolSummary ToResolvedSymbol(this ISymbol symbol)
    {
        var (filePath, line, column) = symbol.GetDeclarationPosition();
        return new ResolvedSymbolSummary(
            SymbolIdentity.CreateId(symbol),
            symbol.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.MinimallyQualifiedFormat),
            symbol.Kind.ToString(),
            filePath,
            line,
            column);
    }

    public static DiagnosticsSummary ToDiagnosticsSummary(this IReadOnlyList<DiagnosticItem> diagnostics)
    {
        var error = diagnostics.Count(static d => string.Equals(d.Severity, "error", StringComparison.OrdinalIgnoreCase));
        var warning = diagnostics.Count(static d => string.Equals(d.Severity, "warning", StringComparison.OrdinalIgnoreCase));
        var info = diagnostics.Count - error - warning;
        return new DiagnosticsSummary(error, warning, info, diagnostics.Count);
    }
}
