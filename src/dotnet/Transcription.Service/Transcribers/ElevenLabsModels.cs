using System.Text.Json.Serialization;

namespace ActualChat.Transcription;

public sealed class ElevenLabsMessage
{
    [JsonPropertyName("message_type")] public string? MessageType { get; set; }
    [JsonPropertyName("session_id")] public string? SessionId { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("language_code")] public string? LanguageCode { get; set; }
    [JsonPropertyName("words")] public ElevenLabsWord[]? Words { get; set; }
}

public sealed class ElevenLabsWord
{
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("start")] public double Start { get; set; }
    [JsonPropertyName("end")] public double End { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("speaker_id")] public string? SpeakerId { get; set; }
}
