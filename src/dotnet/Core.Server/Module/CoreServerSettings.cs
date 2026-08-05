using ActualLab.IO;

namespace ActualChat.Module;

// AI provider credentials live here rather than per-feature: one account per provider serves
// transcription, translation, summarization and search, and every server project references Core.Server.

public sealed class CoreServerSettings
{
    public bool UseNatsQueues { get; set; } = true;
    public string GoogleStorageBucket { get; set; } = "";
    public string GoogleProjectId { get; set; } = "";
    public string GoogleRegionId { get; set; } = "us-central1";
    public bool UseGoogleTranscoder { get; set; }
    public FilePath PromptsDir { get; set; }
    public string OpenAIKey { get; set; } = "";
    public string OpenAIProxy { get; set; } = "";
    public string AnthropicKey { get; set; } = "";
    public string DeepgramKey { get; set; } = "";
    public string SonioxKey { get; set; } = "";
    public string ElevenLabsKey { get; set; } = "";
}
