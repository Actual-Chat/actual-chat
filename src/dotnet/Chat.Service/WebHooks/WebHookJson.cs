using System.Text.Encodings.Web;

namespace ActualChat.Chat;

public static class WebHookJson
{
    public static readonly JsonSerializerOptions Options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        // Server-to-server JSON, never embedded in HTML - safe to skip the default \uXXXX escaping of non-ASCII text.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
