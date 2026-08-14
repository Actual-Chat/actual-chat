
namespace ActualChat.Transcription;

public sealed class SonioxToken
{
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("start_ms")] public long StartMs { get; set; }
    [JsonPropertyName("end_ms")] public long EndMs { get; set; }
    [JsonPropertyName("is_final")] public bool IsFinal { get; set; }
    [JsonPropertyName("language")] public string? Language { get; set; }
}

public sealed class SonioxResponse
{
    [JsonPropertyName("tokens")] public SonioxToken[]? Tokens { get; set; }
    [JsonPropertyName("finished")] public bool Finished { get; set; }
    [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("total_audio_proc_ms")] public long? TotalAudioProcMs { get; set; }
}

public sealed class SonioxContextGeneralItem
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
}

public sealed class SonioxContextPayload
{
    [JsonPropertyName("general")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SonioxContextGeneralItem[]? General { get; set; }
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }
    [JsonPropertyName("terms")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Terms { get; set; }
}

public sealed class SonioxIdResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}

public sealed class SonioxStatusResponse
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
}

public sealed class SonioxFile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("filename")] public string? Filename { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class SonioxFileList
{
    [JsonPropertyName("files")] public SonioxFile[]? Files { get; set; }
    // Opaque - valid only when passed back verbatim, so it's never parsed or rebuilt.
    [JsonPropertyName("next_page_cursor")] public string? NextPageCursor { get; set; }
}
