using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace RoslynMcp.Infrastructure.Agent;

internal sealed class QualifiedSymbolQuery
{
    private QualifiedSymbolQuery(
        string normalizedText,
        string lookupName,
        IReadOnlyList<QualifiedNameSegment> containerSegments,
        QualifiedNameSegment finalSegment,
        IReadOnlyList<string> parameterTypes,
        bool hasExplicitParameterList)
    {
        NormalizedText = normalizedText;
        LookupName = lookupName;
        ContainerSegments = containerSegments;
        FinalSegment = finalSegment;
        ParameterTypes = parameterTypes;
        HasExplicitParameterList = hasExplicitParameterList;
    }

    public string NormalizedText { get; }

    public string LookupName { get; }

    public IReadOnlyList<QualifiedNameSegment> ContainerSegments { get; }

    public QualifiedNameSegment FinalSegment { get; }

    public IReadOnlyList<string> ParameterTypes { get; }

    public bool HasExplicitParameterList { get; }

    public bool IsShortNameOnly => ContainerSegments.Count == 0 && !HasExplicitParameterList && FinalSegment.GenericArity is null;

    public static QualifiedSymbolQuery Parse(string normalizedQualifiedName)
    {
        var segments = SplitSegments(normalizedQualifiedName);
        if (segments.Count == 0)
        {
            return new QualifiedSymbolQuery(
                normalizedQualifiedName,
                normalizedQualifiedName,
                Array.Empty<QualifiedNameSegment>(),
                new QualifiedNameSegment(normalizedQualifiedName, null),
                Array.Empty<string>(),
                false);
        }

        var lastSegment = segments[^1];
        var hasExplicitParameterList = TryExtractParameterList(lastSegment, out var memberName, out var parameterListText);
        var finalSegment = ParseSegment(hasExplicitParameterList ? memberName : lastSegment);
        var containerSegments = segments
            .Take(segments.Count - 1)
            .Select(ParseSegment)
            .ToArray();

        return new QualifiedSymbolQuery(
            normalizedQualifiedName,
            finalSegment.Name,
            containerSegments,
            finalSegment,
            hasExplicitParameterList ? SplitParameterList(parameterListText).ToArray() : Array.Empty<string>(),
            hasExplicitParameterList);
    }

    public bool Matches(ISymbol symbol)
    {
        if (symbol.MatchesQualifiedName(NormalizedText))
        {
            return true;
        }

        if (MatchesType(symbol))
        {
            return true;
        }

        return MatchesMember(symbol);
    }

    private bool MatchesType(ISymbol symbol)
    {
        if (HasExplicitParameterList || symbol is not INamedTypeSymbol namedType)
        {
            return false;
        }

        if (!FinalSegment.Matches(namedType.Name, namedType.Arity))
        {
            return false;
        }

        return ContainerSegments.Count == 0 || ContainerSegmentsMatch(namedType.GetQualifiedTypeSegments(includeSelf: false));
    }

    private bool MatchesMember(ISymbol symbol)
    {
        if (symbol is INamedTypeSymbol or INamespaceSymbol)
        {
            return false;
        }

        if (!MatchesMemberName(symbol))
        {
            return false;
        }

        if (ContainerSegments.Count > 0 && !ContainerSegmentsMatch(symbol.GetQualifiedContainerSegments()))
        {
            return false;
        }

        if (!HasExplicitParameterList)
        {
            return true;
        }

        return symbol is IMethodSymbol method && ParametersMatch(method.Parameters);
    }

    private bool MatchesMemberName(ISymbol symbol)
    {
        if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } method)
        {
            return string.Equals(method.ContainingType?.Name, FinalSegment.Name, StringComparison.Ordinal);
        }

        return string.Equals(symbol.Name, FinalSegment.Name, StringComparison.Ordinal);
    }

    private bool ParametersMatch(ImmutableArray<IParameterSymbol> parameters)
    {
        if (parameters.Length != ParameterTypes.Count)
        {
            return false;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            if (!ParameterTypeMatches(ParameterTypes[i], parameters[i].Type))
            {
                return false;
            }
        }

        return true;
    }

    private bool ParameterTypeMatches(string requestedType, ITypeSymbol parameterType)
    {
        var normalizedRequestedType = requestedType.NormalizeQualifiedName().RemoveAllWhitespace();
        return parameterType
            .GetComparableTypeNames()
            .Any(candidate => string.Equals(candidate, normalizedRequestedType, StringComparison.Ordinal));
    }

    private bool ContainerSegmentsMatch(IReadOnlyList<QualifiedNameSegment> actualSegments)
    {
        if (actualSegments.Count != ContainerSegments.Count)
        {
            return false;
        }

        for (var i = 0; i < actualSegments.Count; i++)
        {
            if (!ContainerSegments[i].Matches(actualSegments[i].Name, actualSegments[i].GenericArity))
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> SplitSegments(string value)
    {
        var segments = new List<string>();
        var start = 0;
        var angleDepth = 0;
        var parenDepth = 0;

        for (var i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '<':
                    angleDepth++;
                    break;
                case '>':
                    angleDepth = Math.Max(0, angleDepth - 1);
                    break;
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    parenDepth = Math.Max(0, parenDepth - 1);
                    break;
                case '.' when angleDepth == 0 && parenDepth == 0:
                    segments.Add(value[start..i]);
                    start = i + 1;
                    break;
            }
        }

        segments.Add(value[start..]);
        return segments
            .Select(static segment => segment.Trim())
            .Where(static segment => segment.Length > 0)
            .ToList();
    }

    private static QualifiedNameSegment ParseSegment(string segment)
    {
        var trimmed = segment.Trim();
        var genericStart = trimmed.IndexOf('<');
        if (genericStart < 0 || !trimmed.EndsWith('>'))
        {
            return new QualifiedNameSegment(trimmed, null);
        }

        var name = trimmed[..genericStart].Trim();
        var genericArguments = trimmed[(genericStart + 1)..^1];
        return new QualifiedNameSegment(name, CountTopLevelItems(genericArguments));
    }

    private static bool TryExtractParameterList(string value, out string memberName, out string parameterListText)
    {
        var openParen = value.IndexOf('(');
        if (openParen < 0 || !value.EndsWith(')'))
        {
            memberName = value;
            parameterListText = string.Empty;
            return false;
        }

        memberName = value[..openParen].Trim();
        parameterListText = value[(openParen + 1)..^1].Trim();
        return true;
    }

    private static IEnumerable<string> SplitParameterList(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var start = 0;
        var angleDepth = 0;
        var parenDepth = 0;
        var bracketDepth = 0;

        for (var i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '<':
                    angleDepth++;
                    break;
                case '>':
                    angleDepth = Math.Max(0, angleDepth - 1);
                    break;
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    parenDepth = Math.Max(0, parenDepth - 1);
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    break;
                case ',' when angleDepth == 0 && parenDepth == 0 && bracketDepth == 0:
                    yield return value[start..i].Trim();
                    start = i + 1;
                    break;
            }
        }

        yield return value[start..].Trim();
    }

    private static int CountTopLevelItems(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var count = 1;
        var angleDepth = 0;
        var parenDepth = 0;
        var bracketDepth = 0;

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '<':
                    angleDepth++;
                    break;
                case '>':
                    angleDepth = Math.Max(0, angleDepth - 1);
                    break;
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    parenDepth = Math.Max(0, parenDepth - 1);
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    break;
                case ',' when angleDepth == 0 && parenDepth == 0 && bracketDepth == 0:
                    count++;
                    break;
            }
        }

        return count;
    }
}

internal readonly record struct QualifiedNameSegment(string Name, int? GenericArity)
{
    public bool Matches(string actualName, int? actualGenericArity)
    {
        if (!string.Equals(Name, actualName, StringComparison.Ordinal))
        {
            return false;
        }

        return GenericArity is null || GenericArity == actualGenericArity;
    }
}
