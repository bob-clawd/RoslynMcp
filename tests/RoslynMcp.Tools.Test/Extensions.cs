using System.Text.Encodings.Web;
using System.Text.Json;

namespace RoslynMcp.Tools.Test;

public static class Extensions
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    
    internal static string ToJson(this object result) => JsonSerializer.Serialize(result, Options);
}
