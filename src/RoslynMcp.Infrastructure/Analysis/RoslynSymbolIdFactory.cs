using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynMcp.Infrastructure.Agent;
using System.Reflection;

namespace RoslynMcp.Infrastructure.Analysis;

/// <summary>
/// Factory for creating stable symbol IDs from Roslyn ISymbol instances.
/// Uses Roslyn's internal SymbolKey.CreateString for serialization.
/// </summary>
internal sealed class RoslynSymbolIdFactory : IRoslynSymbolIdFactory
{
    private static readonly MethodInfo CreateString;

    static RoslynSymbolIdFactory()
    {
        var assembly = typeof(SymbolFinder).Assembly;
        var symbolKeyType = assembly.GetType("Microsoft.CodeAnalysis.SymbolKey", throwOnError: true)!;
        CreateString = symbolKeyType.GetMethod("CreateString", BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(ISymbol), typeof(CancellationToken)],
            modifiers: null)
            ?? throw new InvalidOperationException("Unable to locate SymbolKey.CreateString");
    }

    public string CreateId(ISymbol symbol)
    {
        var resolved = symbol.OriginalDefinition ?? symbol;
        var result = (string?)CreateString.Invoke(null, [resolved, CancellationToken.None]);

        if (!string.IsNullOrEmpty(result))
            return result.ToExternal();

        return resolved.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).ToExternal();
    }
}
