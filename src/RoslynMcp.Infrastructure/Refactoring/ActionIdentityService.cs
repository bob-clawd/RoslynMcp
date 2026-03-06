using RoslynMcp.Core.Models;
using System.Text;

namespace RoslynMcp.Infrastructure.Refactoring;

internal sealed class ActionIdentityService
{
    public string Create(int workspaceVersion, string policyProfile, DiscoveredAction action)
        => string.Join('|',
            "v1",
            workspaceVersion,
            Encode(policyProfile),
            Encode(action.Origin),
            Encode(action.Category),
            Encode(action.ProviderActionKey),
            action.SpanStart,
            action.SpanLength,
            Encode(action.FilePath),
            Encode(action.DiagnosticId),
            Encode(action.RefactoringId),
            action.Location.Line,
            action.Location.Column);

    public ActionExecutionIdentity? Parse(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return null;
        }

        var parts = actionId.Split('|');
        if (parts.Length != 13 || !string.Equals(parts[0], "v1", StringComparison.Ordinal))
        {
            return null;
        }

        if (!int.TryParse(parts[1], out var workspaceVersion)
            || !int.TryParse(parts[6], out var spanStart)
            || !int.TryParse(parts[7], out var spanLength)
            || !int.TryParse(parts[11], out var line)
            || !int.TryParse(parts[12], out var column))
        {
            return null;
        }

        var policyProfile = Decode(parts[2]);
        var origin = Decode(parts[3]);
        var category = Decode(parts[4]);
        var providerKey = Decode(parts[5]);
        var filePath = Decode(parts[8]);
        if (string.IsNullOrWhiteSpace(origin)
            || string.IsNullOrWhiteSpace(category)
            || string.IsNullOrWhiteSpace(providerKey)
            || string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        return new ActionExecutionIdentity(
            workspaceVersion,
            string.IsNullOrWhiteSpace(policyProfile) ? "default" : policyProfile,
            origin,
            category,
            providerKey,
            filePath,
            spanStart,
            spanLength,
            NormalizeNullable(Decode(parts[9])),
            NormalizeNullable(Decode(parts[10])),
            new SourceLocation(filePath, line, column));
    }

    private static string Encode(string? value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

    private static string Decode(string encoded)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    private static string? NormalizeNullable(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
