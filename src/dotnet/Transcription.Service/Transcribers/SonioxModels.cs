
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

public sealed class SonioxTranscription
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("file_id")] public string? FileId { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class SonioxTranscriptionList
{
    [JsonPropertyName("transcriptions")] public SonioxTranscription[]? Transcriptions { get; set; }
    // Opaque - valid only when passed back verbatim, so it's never parsed or rebuilt.
    [JsonPropertyName("next_page_cursor")] public string? NextPageCursor { get; set; }
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
    [JsonPropertyName("next_page_cursor")] public string? NextPageCursor { get; set; }
}

public sealed class SonioxSharedVoice
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("gender")] public string? Gender { get; set; }
    [JsonPropertyName("age")] public string? Age { get; set; }
    [JsonPropertyName("accent")] public string? Accent { get; set; }
    [JsonPropertyName("use_case")] public string[]? UseCase { get; set; }
    [JsonPropertyName("style")] public string[]? Style { get; set; }
}

public sealed class SonioxSharedVoiceList
{
    [JsonPropertyName("voices")] public SonioxSharedVoice[]? Voices { get; set; }
    [JsonPropertyName("next_page_cursor")] public string? NextPageCursor { get; set; }
}

public sealed class SonioxVoiceModelStatus
{
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("error_type")] public string? ErrorType { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
}

// Observed live on 2026-09-15 (POST/GET /v1/voices/{id}):
// {"id": "<uuid>", "name": "voxt-probe-...", "filename": "probe.wav",
//  "created_at": "2026-09-15T14:53:55.331Z",
//  "models": [{"model": "tts-rt-v2", "status": "processing", "error_type": null, "error_message": null}]}
// status goes "processing" -> "ready" (seconds later); "failed" is documented but not observed here.
public sealed class SonioxVoiceResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("models")] public SonioxVoiceModelStatus[]? Models { get; set; }
}

public sealed class SonioxVoiceList
{
    [JsonPropertyName("voices")] public SonioxVoiceResponse[]? Voices { get; set; }
    [JsonPropertyName("next_page_cursor")] public string? NextPageCursor { get; set; }
}

public sealed class SonioxTtsResponse
{
    [JsonPropertyName("stream_id")] public string? StreamId { get; set; }
    [JsonPropertyName("audio")] public string? Audio { get; set; }
    [JsonPropertyName("audio_end")] public bool AudioEnd { get; set; }
    [JsonPropertyName("terminated")] public bool Terminated { get; set; }
    [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
    [JsonPropertyName("error_type")] public string? ErrorType { get; set; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
}
