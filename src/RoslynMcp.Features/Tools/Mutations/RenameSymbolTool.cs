using System.ComponentModel;
using ModelContextProtocol.Server;
using RoslynMcp.Core;
using RoslynMcp.Core.Contracts;
using RoslynMcp.Core.Models;

namespace RoslynMcp.Features.Tools.Mutations;

public sealed class RenameSymbolTool(IRefactoringService refactoringService) : Tool
{
    private readonly IRefactoringService _refactoringService = refactoringService ?? throw new ArgumentNullException(nameof(refactoringService));

    [McpServerTool(Name = "rename_symbol", Title = "Rename Symbol")]
    [Description("Use this tool when you need to rename a symbol (type, method, property, field, parameter, local variable, etc.) across the entire solution. This performs a safe refactoring that updates all references to the symbol.  Best for: When you don't know all places where a type is used – it finds every reference automatically.")]
    public Task<RenameSymbolResult> ExecuteAsync(CancellationToken cancellationToken,
        [Description("The symbol ID of the symbol to rename. Use 'resolve_symbol' to obtain this if needed.")]
        string symbolId,
        [Description("The new name for the symbol. Must be a valid C# identifier and should not conflict with existing symbols in the same scope.")]
        string newName
        )
    {
        return _refactoringService.RenameSymbolAsync(symbolId.ToRenameSymbolRequest(newName), cancellationToken);
    }
}