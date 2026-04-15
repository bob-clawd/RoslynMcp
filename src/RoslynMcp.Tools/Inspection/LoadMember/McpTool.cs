using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;
using RoslynMcp.Tools.Extensions;
using RoslynMcp.Tools.Managers;

namespace RoslynMcp.Tools.Inspection.LoadMember;

public sealed record Result(
    MemberSymbol? Symbol,
    SymbolDocumentation? Documentation,
    IReadOnlyList<MemberSymbol> Callers,
    IReadOnlyList<MemberSymbol> Callees,
    IReadOnlyList<MemberSymbol> Overrides,
    IReadOnlyList<MemberSymbol> Implementations,
    ErrorInfo? Error = null)
{
    public static Result AsError(string message, IReadOnlyDictionary<string, string>? details = null)
        => new(null, null, [], [], [], [], new ErrorInfo(message, details));
}

[McpServerToolType]
public sealed class McpTool(WorkspaceManager workspaceManager, SolutionManager solutionManager, SymbolManager symbolManager) : Tool
{
    [McpServerTool(Name = "load_member", Title = "Load Member", ReadOnly = true, Idempotent = true)]
    [Description("Use this tool when you need callers/callees or overrides/implementations of a symbol.")]
    public async Task<Result> Execute(CancellationToken cancellationToken,
        [Description("The stable symbol ID, obtained from load_type or from a search_member match.")]
        string? memberSymbolId = null
        )
    {
        if (solutionManager.Solution is not { } solution)
            return Result.AsError("load solution first");

        var symbol = symbolManager.ToSymbol(memberSymbolId);

        if (symbol is null)
            return Result.AsError("symbol not found");

        if (symbol is ITypeSymbol)
            return Result.AsError("no type symbol please");

        var callersTask = SymbolFinder.FindCallersAsync(symbol, solution, cancellationToken);
        var calleesTask = CollectCalleesAsync(symbol, solution, cancellationToken);
        var overridesTask = SymbolFinder.FindOverridesAsync(symbol, solution, null, cancellationToken);
        var implementationsTask = SymbolFinder.FindImplementationsAsync(symbol, solution, null, cancellationToken);
        
        try
        {
            await Task.WhenAll(callersTask, calleesTask, overridesTask, implementationsTask).ConfigureAwait(false);
            
            return new Result(
                MemberSymbol.From(symbol, symbolManager, workspaceManager),
                symbol.GetDocumentation(),
                ToMembers((await callersTask).Select(caller => caller.CallingSymbol)),
                ToMembers((await calleesTask).Select(callee => callee.Symbol)),
                ToMembers(await overridesTask),
                ToMembers(await implementationsTask));
        }
        catch (Exception e)
        {
            return Result.AsError(e.Message, new Dictionary<string, string>
            {
                ["inner exception"] = e.InnerException?.Message ?? string.Empty,
                ["stack trace"] = e.StackTrace ?? string.Empty
            });
        }
    }

    private IReadOnlyList<MemberSymbol> ToMembers(IEnumerable<ISymbol> symbols) => symbols
        .Select(symbol => MemberSymbol.From(symbol, symbolManager, workspaceManager))
        .Where(member => member.Kind is not null && !member.Location.IsNullOrEmpty())
        .DistinctBy(member => member.SymbolId)
        .OrderBy(member => member.Location, StringComparer.OrdinalIgnoreCase)
        .ThenBy(member => member.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static async Task<IReadOnlyList<(ISymbol Symbol, Location Location)>> CollectCalleesAsync(ISymbol symbol, Solution solution, CancellationToken ct)
    {
        var results = new List<(ISymbol, Location)>();

        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            ct.ThrowIfCancellationRequested();
            var node = await reference.GetSyntaxAsync(ct).ConfigureAwait(false);
            
            if( solution.GetDocument(node.SyntaxTree) is not { } document)
                continue;

            if(await document.GetSemanticModelAsync(ct) is not { } semanticModel)
                continue;

            var collector = new CalleeCollector(semanticModel, ct);
            collector.Visit(node);
            results.AddRange(collector.Callees);
        }

        return results;
    }
    
    private sealed class CalleeCollector : CSharpSyntaxWalker
    {
        private readonly SemanticModel _semanticModel;
        private readonly CancellationToken _cancellationToken;
        private readonly List<(ISymbol Symbol, Location Location)> _callees = [];

        internal CalleeCollector(SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            _semanticModel = semanticModel;
            _cancellationToken = cancellationToken;
        }

        public IReadOnlyList<(ISymbol Symbol, Location Location)> Callees => _callees;

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            RecordSymbol(node.Expression, node.GetLocation());
            base.VisitInvocationExpression(node);
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            RecordSymbol(node, node.GetLocation());
            base.VisitObjectCreationExpression(node);
        }

        private void RecordSymbol(ExpressionSyntax expression, Microsoft.CodeAnalysis.Location location)
        {
            if (_cancellationToken.IsCancellationRequested)
                return;

            var info = ModelExtensions.GetSymbolInfo(_semanticModel, expression, _cancellationToken);
            
            var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
            
            if (symbol == null || !location.IsInSource)
                return;

            _callees.Add((symbol.OriginalDefinition ?? symbol, location));
        }
    }
}
