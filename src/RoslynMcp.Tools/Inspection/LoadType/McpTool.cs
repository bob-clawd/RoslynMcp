using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.LoadType;

public sealed record Result(
    TypeSymbol? Symbol,
    string? Documentation,
    IReadOnlyList<TypeSymbol> Derived,
    IReadOnlyList<TypeSymbol> Implementations,
    IReadOnlyList<MemberSymbol> Members,
    ErrorInfo? Error = null)
{
    public static Result AsError(string message, IReadOnlyDictionary<string, string>? details = null)
        => new(null, null, [], [], [], new ErrorInfo(message, details));
}

[McpServerToolType]
public sealed class McpTool(WorkspaceManager workspaceManager, SolutionManager solutionManager, SymbolManager symbolManager) : Tool
{
    [McpServerTool(Name = "load_type", Title = "Load Type", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need to inspect type hierarchy and members declared by the specific type.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("The stable symbol ID of a type, obtained from load_project.")]
        string? typeSymbolId = null)
    {
        if (solutionManager.Solution is not { } solution)
            return Result.AsError("load solution first");
        
        if (symbolManager.ToSymbol(typeSymbolId) is not INamedTypeSymbol symbol)
            return Result.AsError("type not found");

        var derivedClassesTask = SymbolFinder.FindDerivedClassesAsync(symbol, solution, cancellationToken: cancellationToken);
        var derivedInterfacesTask = SymbolFinder.FindDerivedInterfacesAsync(symbol, solution, cancellationToken: cancellationToken);
        var implementationsTask = SymbolFinder.FindImplementationsAsync(symbol, solution, cancellationToken: cancellationToken);

        await Task.WhenAll(derivedClassesTask, derivedInterfacesTask, implementationsTask).ConfigureAwait(false);

        var documentation = symbol.GetDocumentation();
        
        return new Result(TypeSymbol.From(symbol, symbolManager, workspaceManager),
            documentation?.Summary,
            ToTypes((await derivedClassesTask).Concat(await derivedInterfacesTask)),
            ToTypes(await implementationsTask),
            ToMembers(symbol.GetMembers()));
    }

    private IReadOnlyList<TypeSymbol> ToTypes(IEnumerable<INamedTypeSymbol> symbols)
    {
        var types = symbols
            .Select(symbol => TypeSymbol.From(symbol, symbolManager, workspaceManager))
            .Where(type => !type.Location.IsNullOrEmpty())
            .DistinctBy(type => type.SymbolId)
            .OrderBy(type => type.Location, StringComparer.OrdinalIgnoreCase)
            .ThenBy(type => type.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (types.Any(type => type.Location.IsHandwritten()))
            types.RemoveAll(type => !type.Location.IsHandwritten());

        return types;
    }

    private IReadOnlyList<MemberSymbol> ToMembers(IEnumerable<ISymbol> symbols) => symbols
        .Select(symbol => MemberSymbol.From(symbol, symbolManager, workspaceManager))
        .Where(symbol => symbol.Kind is not null && symbol.Location.IsHandwritten())
        .OrderBy(symbol => symbol.Location, StringComparer.OrdinalIgnoreCase)
        .ThenBy(symbol => symbol.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
