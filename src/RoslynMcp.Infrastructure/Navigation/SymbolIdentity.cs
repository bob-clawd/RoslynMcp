using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using System.Reflection;

namespace RoslynMcp.Infrastructure.Navigation;

internal static class SymbolIdentity
{
    private static readonly MethodInfo _createString;
    private static readonly MethodInfo _resolveString;
    private static readonly PropertyInfo _resolutionSymbol;

    static SymbolIdentity()
    {
        var assembly = typeof(SymbolFinder).Assembly;
        var symbolKeyType = assembly.GetType("Microsoft.CodeAnalysis.SymbolKey", throwOnError: true)!;
        var resolutionType = assembly.GetType("Microsoft.CodeAnalysis.SymbolKeyResolution", throwOnError: true)!;

        _createString = symbolKeyType.GetMethod("CreateString", BindingFlags.Public | BindingFlags.Static,
                             binder: null,
                             types: [typeof(ISymbol), typeof(CancellationToken)],
                             modifiers: null)
            ?? throw new InvalidOperationException("Unable to locate SymbolKey.CreateString");

        _resolveString = symbolKeyType.GetMethod("ResolveString", BindingFlags.Public | BindingFlags.Static,
                              binder: null,
                              types: [typeof(string), typeof(Compilation), typeof(bool), typeof(CancellationToken)],
                              modifiers: null)
            ?? throw new InvalidOperationException("Unable to locate SymbolKey.ResolveString");

        _resolutionSymbol = resolutionType.GetProperty("Symbol", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Unable to locate SymbolKeyResolution.Symbol");
    }

    public static string CreateId(this ISymbol symbol)
    {
        var resolved = symbol.OriginalDefinition ?? symbol;
        var result = (string?)_createString.Invoke(null, [resolved, CancellationToken.None]);
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

        var resolution = _resolveString.Invoke(null, [identifier, compilation, true, ct]);
        if (resolution == null)
        {
            return null;
        }

        return (ISymbol?)_resolutionSymbol.GetValue(resolution);
    }
}
