using ActualLab.IO;

namespace ActualChat.Chat.Module;

public sealed class ChatSettings
{
    public string OpenAIKey { get; set; } = "";
    public string OpenAIModel { get; set; } = "o4-mini";
    public string OpenAIProxy { get; set; } = "";
    public bool IsTranslationEnabled { get; set; }
    public bool UseFakeLanguageDetection { get; set; }
    public TranslationSettings Translation { get; set; } = new ();
    public LanguageDetectionSettings LanguageDetection { get; set; } = new ();
    public bool IsSummarizationEnabled { get; set; }
    public SummarizationSettings Summarization { get; set; } = new ();
    public bool IsRetranscriptionEnabled { get; set; }
    public RetranscriptionSettings Retranscription { get; set; } = new ();
    public bool IsChatContentItemIndexingEnabled { get; set; }
}

public class TranslationSettings
{
    public string OpenAIKey { get; set; } = "";
    public string OpenAIModel { get; set; } = "gpt-4.1";
    public FilePath PromptFile { get; set; } = "translate.md";
    public FilePath RealtimePromptFile { get; set; } = "translate-realtime.md";
    public int ContentMinLengthWithoutContext { get; set; } = 150;
    public int ContextMessageCount { get; set; } = 5;
    public int OpenAIModelMaxTokens { get; set; } = 32768;
    public int RealtimeOpenAIModelMaxTokens { get; set; } = 4192;
    public string RealtimeOpenAIModel { get; set; } = "gpt-4.1-mini";
    public string RealtimeGeminiModel { get; set; } = "";
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int StreamingMinContentLength { get; set; } = 50;
    public TimeSpan HangingTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);
}

public class LanguageDetectionSettings
{
    public string OpenAIKey { get; set; } = "";
    public string OpenAIModel { get; set; } = "gpt-4.1-nano";
    public FilePath PromptFile { get; set; } = "detect-languages.md";
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromMinutes(3);
}

public class SummarizationSettings
{
    public string OpenAIKey { get; set; } = "";
    public string OpenAIModel { get; set; } = "gpt-4.1";
    public int MinConversationWords { get; set; } = 1200;
    public int MinConversationEntries { get; set; } = 10;
    public TimeSpan ChatEntrySummarizationDelay { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan ResummarizationDelay { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan ChatEntrySummarizationDelayQuanta { get; set; } = TimeSpan.FromMinutes(1);
    public FilePath SummarizeConversationPromptFile { get; set; } = "summarize-conversation.md";
    public FilePath SummarizeChatDigestPromptFile { get; set; } = "summarize-chat-digest.md";
    public FilePath SuggestChatThreadTitlePromptFile { get; set; } = "suggest-thread-title.md";
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromMinutes(5);
}

public class RetranscriptionSettings
{
    public string OpenAIKey { get; set; } = "";
    public string OpenAIModel { get; set; } = "gpt-4o-transcribe";
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromMinutes(3);
}
