namespace RoslynMcp.Tools.Extensions;

internal static class FileExtensions
{
    private static readonly string[] SubFolders =
    [
        "bin",
        "obj"
    ];

    private static readonly string[] GeneratedFileSuffixes =
    [
        ".g.cs",
        ".g.i.cs",
        ".generated.cs",
        ".designer.cs",
        ".AssemblyAttributes.cs",
        ".AssemblyInfo.cs"
    ];

    internal enum SourceKind
    {
        HandWritten,
        Generated,
        Intermediate,
        Unknown
    }

    extension(string? path)
    {
        internal IEnumerable<string> DiscoverFiles(params string[] patterns)
        {
            if (path is null)
                yield break;
            
            foreach (var pattern in patterns)
            {
                foreach (var solutionPath in Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories))
                {
                    yield return Path.GetFullPath(solutionPath);
                }
            }
        }

        internal bool IsHandwritten() => path.Classify() is SourceKind.HandWritten or SourceKind.Unknown;

        private SourceKind Classify()
        {
            if (string.IsNullOrWhiteSpace(path) ||
                path.NormalizePathSeparators() is not { } normalized ||
                Path.GetFileName(normalized) is not { } fileName)
            {
                return SourceKind.Unknown;
            }

            if (SubFolders.Any(subFolder => normalized.Contains($"{Path.DirectorySeparatorChar}{subFolder}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)))
                return SourceKind.Intermediate;

            if (GeneratedFileSuffixes.Any(suffix => fileName.Contains(suffix, StringComparison.OrdinalIgnoreCase)))
                return SourceKind.Generated;

            return SourceKind.HandWritten;
        }
    }
}