namespace RoslynMcp.Core;

internal static class TypeSyntaxStringExtensions
{
    internal static string NormalizeEscapedTypeSyntax(this string input) => input
        .Replace("&lt;", "<", StringComparison.Ordinal)
        .Replace("&gt;", ">", StringComparison.Ordinal);
}
