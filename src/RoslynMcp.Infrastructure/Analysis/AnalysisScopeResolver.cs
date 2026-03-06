using RoslynMcp.Core.Models;
using RoslynMcp.Infrastructure.Navigation;
using Microsoft.CodeAnalysis;

namespace RoslynMcp.Infrastructure.Analysis;

internal sealed class AnalysisScopeResolver : IAnalysisScopeResolver
{
    public bool IsValidScope(string scope)
        => string.Equals(scope, AnalysisScopes.Document, StringComparison.Ordinal)
           || string.Equals(scope, AnalysisScopes.Project, StringComparison.Ordinal)
           || string.Equals(scope, AnalysisScopes.Solution, StringComparison.Ordinal);

    public IEnumerable<Project> ResolveProjectsForScope(Solution solution, string scope, string? path)
    {
        if (string.Equals(scope, AnalysisScopes.Solution, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(path))
        {
            return solution.Projects;
        }

        if (string.Equals(scope, AnalysisScopes.Project, StringComparison.Ordinal))
        {
            return solution.Projects.Where(project =>
                project.FilePath.MatchesByNormalizedPath(path)
                || string.Equals(project.Name, path, StringComparison.OrdinalIgnoreCase));
        }

        return solution.Projects.Where(project =>
            project.Documents.Any(document => document.FilePath.MatchesByNormalizedPath(path)));
    }

    public IReadOnlyList<DiagnosticItem> FilterDiagnosticsByScope(IReadOnlyList<DiagnosticItem> diagnostics, string scope, string? path)
    {
        if (string.Equals(scope, AnalysisScopes.Solution, StringComparison.Ordinal)
            || string.Equals(scope, AnalysisScopes.Project, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(path))
        {
            return diagnostics;
        }

        if (string.Equals(scope, AnalysisScopes.Document, StringComparison.Ordinal))
        {
            return diagnostics.Where(diag => diag.Location.FilePath.MatchesByNormalizedPath(path)).ToList();
        }

        return diagnostics;
    }

    public bool IsDocumentInScope(Document document, string scope, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || string.Equals(scope, AnalysisScopes.Solution, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(scope, AnalysisScopes.Document, StringComparison.Ordinal))
        {
            return document.FilePath.MatchesByNormalizedPath(path);
        }

        var projectPath = document.Project.FilePath;
        return projectPath.MatchesByNormalizedPath(path)
               || string.Equals(document.Project.Name, path, StringComparison.OrdinalIgnoreCase);
    }
}
