namespace RoslynMcp.Tools.Extensions;

internal static class PathExtensions
{
    internal static string? NormalizePathSeparators(this string? input)
    {
        return input?
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    internal static bool IsNullOrEmpty(this string? input)
    {
        return string.IsNullOrEmpty(input);
    }

    internal static bool IsWithin(this string? input, string? root)
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
