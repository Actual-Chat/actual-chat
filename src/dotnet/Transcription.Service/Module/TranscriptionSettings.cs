namespace ActualChat.Transcription.Module;

// Rankings and overrides are comma-separated TranscriberId lists so they stay expressible
// as plain appsettings values and TranscriptionSettings__* environment overrides.

/// <summary>
/// Transcription provider credentials and the per-stage, per-language preference ranking.
/// </summary>
public class TranscriptionSettings
{
    public bool UseFakeTranscriber { get; set; }
    public string StreamRanking { get; set; } = "soniox-stream,deepgram-stream,google-stream";
    public string OfflineRanking { get; set; } = "soniox-offline,openai-offline";
    public Dictionary<string, string> StreamRankingOverrides { get; set; } = new();
    public Dictionary<string, string> OfflineRankingOverrides { get; set; } = new();
    // The built-in Soniox voice used for speakers without a cloned voice
    public string SonioxTtsVoice { get; set; } = "Adrian";
    // Soniox finalizes 3-5 s behind the speech but practically never revises a tail token older
    // than ~1 s; a dub speaks a promoted token, so this is the margin between latency and a
    // spoken revision
    public TimeSpan SonioxStableTokenAge { get; set; } = TimeSpan.FromSeconds(1);
}
