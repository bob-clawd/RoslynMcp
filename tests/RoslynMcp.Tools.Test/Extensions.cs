using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoslynMcp.Tools.Test;

public static class Extensions
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    
    extension(object result)
    {
        internal string ToJson() => JsonSerializer.Serialize(result, Options);
    }
}
