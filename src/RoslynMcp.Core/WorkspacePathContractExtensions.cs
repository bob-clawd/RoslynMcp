using RoslynMcp.Core.Models;

namespace RoslynMcp.Core;

public static class WorkspacePathContractExtensions
{
    private static readonly HashSet<string> PathDetailKeys =
    [
        "path",
        "file",
        "filepath",
        "projectpath",
        "solutionpath",
        "selectedsolutionpath",
        "workspaceroot",
        "target",
        "targetpath"
    ];

    private static readonly HashSet<string> PathFieldNames =
    [
        "path",
        "filepath",
        "projectpath",
        "solutionhintpath",
        "solutionpath",
        "selectedsolutionpath",
        "target",
        "workspaceroot"
    ];

    extension(string? path)
    {
        public string ToWorkspaceAbsolutePath(string workspaceRoot)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path!;
            }

            var trimmedPath = path.Trim();
            try
            {
                return Path.IsPathRooted(trimmedPath)
                    ? Path.GetFullPath(trimmedPath)
                    : Path.GetFullPath(trimmedPath, workspaceRoot);
            }
            catch
            {
                return trimmedPath;
            }
        }

        public string ToWorkspaceRelativePathIfPossible(string workspaceRoot)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path!;
            }

            var absolutePath = path.ToWorkspaceAbsolutePath(workspaceRoot);
            if (!Path.IsPathRooted(absolutePath))
            {
                return absolutePath;
            }

            try
            {
                var normalizedWorkspaceRoot = workspaceRoot.EnsureTrailingDirectorySeparator();
                var normalizedAbsolutePath = Path.GetFullPath(absolutePath);

                if (!normalizedAbsolutePath.StartsWith(normalizedWorkspaceRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return normalizedAbsolutePath;
                }

                return Path.GetRelativePath(workspaceRoot, normalizedAbsolutePath);
            }
            catch
            {
                return absolutePath;
            }
        }
    }

    extension(ErrorInfo? error)
    {
        public ErrorInfo? ToWorkspaceRelativePathIfPossible(string workspaceRoot)
        {
            if (error?.Details is null || error.Details.Count == 0)
            {
                return error;
            }

            Dictionary<string, string>? updatedDetails = null;
            foreach (var pair in error.Details)
            {
                if (!ShouldRewritePathDetail(pair.Key, error.Details))
                {
                    continue;
                }

                var outwardPath = pair.Value.ToWorkspaceRelativePathIfPossible(workspaceRoot);
                if (string.Equals(outwardPath, pair.Value, StringComparison.Ordinal))
                {
                    continue;
                }

                updatedDetails ??= new Dictionary<string, string>(error.Details, StringComparer.Ordinal);
                updatedDetails[pair.Key] = outwardPath;
            }

            return updatedDetails is null ? error : error with { Details = updatedDetails };
        }
    }

    public static IReadOnlyList<string> ToWorkspaceRelativePathsIfPossible(this IReadOnlyList<string> paths, string workspaceRoot)
        => paths.Select(path => path.ToWorkspaceRelativePathIfPossible(workspaceRoot)).ToArray();

    private static bool ShouldRewritePathDetail(string key, IReadOnlyDictionary<string, string> details)
    {
        if (PathDetailKeys.Contains(key))
        {
            return true;
        }

        if (!string.Equals(key, "provided", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return details.TryGetValue("field", out var field) && PathFieldNames.Contains(field)
               || details.TryGetValue("parameter", out var parameter) && PathFieldNames.Contains(parameter);
    }

    private static string EnsureTrailingDirectorySeparator(this string path)
        => path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
}
