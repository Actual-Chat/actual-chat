namespace ActualChat.Transcription.Module;

// Rankings and overrides are comma-separated TranscriberId lists so they stay expressible
// as plain appsettings values and TranscriptionSettings__* environment overrides.

/// <summary>
/// Transcription provider credentials and the per-stage, per-language preference ranking.
/// </summary>
public class TranscriptionSettings
{
    public bool UseFakeTranscriber { get; set; }
    public string StreamRanking { get; set; } = "deepgram-stream,google-stream";
    public string OfflineRanking { get; set; } = "openai-offline";
    public Dictionary<string, string> StreamRankingOverrides { get; set; } = new();
    public Dictionary<string, string> OfflineRankingOverrides { get; set; } = new();
    public Dictionary<string, TranscriberKind> KindOverrides { get; set; } = new();
}
