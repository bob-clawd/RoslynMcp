namespace RoslynMcp.Tools.Extensions;

internal static class PathExtensions
{
    extension(string? input)
    {
        internal string? NormalizePathSeparators()
        {
            return input?
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
        }

        internal bool IsNullOrEmpty()
        {
            return string.IsNullOrEmpty(input);
        }

        internal bool IsWithin(string? root)
        {
            if (input.IsNullOrEmpty() || root.IsNullOrEmpty())
                return false;

            var path = Path.GetFullPath(input!);
            var absoluteRoot = Path.GetFullPath(root!);

            if (string.Equals(path, absoluteRoot, StringComparison.OrdinalIgnoreCase))
                return true;

            return path.StartsWith(absoluteRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }
}
