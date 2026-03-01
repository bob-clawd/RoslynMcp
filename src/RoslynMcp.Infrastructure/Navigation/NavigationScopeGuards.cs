using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models.Navigation;

namespace RoslynMcp.Infrastructure.Navigation;

internal static class NavigationScopeGuards
{
    public static bool IsValidSearchScope(string scope)
        => string.Equals(scope, SymbolSearchScopes.Document, StringComparison.Ordinal)
           || string.Equals(scope, SymbolSearchScopes.Project, StringComparison.Ordinal)
           || string.Equals(scope, SymbolSearchScopes.Solution, StringComparison.Ordinal);

    public static bool PathExistsInScope(Solution solution, string scope, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || string.Equals(scope, SymbolSearchScopes.Solution, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(scope, SymbolSearchScopes.Document, StringComparison.Ordinal))
        {
            return solution.Projects
                .SelectMany(static p => p.Documents)
                .Any(document => NavigationModelUtilities.MatchesByNormalizedPath(document.FilePath, path));
        }

        return solution.Projects.Any(project =>
            NavigationModelUtilities.MatchesByNormalizedPath(project.FilePath, path)
            || string.Equals(project.Name, path, StringComparison.OrdinalIgnoreCase));
    }
}
