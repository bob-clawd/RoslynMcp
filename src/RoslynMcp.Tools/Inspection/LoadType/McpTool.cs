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
public sealed class McpTool(
    WorkspaceManager workspaceManager,
    SolutionManager solutionManager,
    SymbolManager symbolManager
    ) : Tool
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

        await Task.WhenAll(derivedClassesTask, derivedInterfacesTask, implementationsTask);

        var derivedClasses = await derivedClassesTask.ConfigureAwait(false);
        var derivedInterfaces = await derivedInterfacesTask.ConfigureAwait(false);
        var implementations = await implementationsTask.ConfigureAwait(false);

        var members = symbol.GetMembers()
            .Select(symbol => MemberSymbol.From(symbol, symbolManager, workspaceManager))
            .Where(symbol => symbol.Kind is not null && symbol.Location.IsHandwritten())
            .ToList();

        var documentation = symbol.GetDocumentation();
        
        return new Result(TypeSymbol.From(symbol, symbolManager, workspaceManager),
            documentation?.Summary,
            derivedClasses.Concat(derivedInterfaces).Select(d => TypeSymbol.From(d, symbolManager, workspaceManager)).ToList(),
            implementations.Select(i => TypeSymbol.From(i, symbolManager, workspaceManager)).ToList(),
            members);
    }
}
