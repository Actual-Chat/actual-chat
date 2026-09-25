using ActualLab.IO;

namespace ActualChat.Chat.Module;

public sealed class ChatSettings
{
    public string OpenAIModel { get; set; } = "gpt-5.6-terra";
    public bool IsTranslationEnabled { get; set; }
    public bool UseFakeLanguageDetection { get; set; }
    public TranslationSettings Translation { get; set; } = new ();
    public LanguageDetectionSettings LanguageDetection { get; set; } = new ();
    public bool IsSummarizationEnabled { get; set; }
    public SummarizationSettings Summarization { get; set; } = new ();
    public CoachSettings Coach { get; set; } = new ();
    public bool IsChatContentItemIndexingEnabled { get; set; }
    // How much of a chat's tail the describer reads. The lower bound is the client's - see
    // Constants.Chat.MinImageSuggestionEntries.
    public int MaxImageSuggestionEntries { get; set; } = 100;
    public TimeSpan ImageSuggestionDismissPeriod { get; set; } = TimeSpan.FromDays(30);
}

public class TranslationSettings
{
    public string OpenAIModel { get; set; } = "gpt-5.6-terra";
    public int OpenAIModelMaxTokens { get; set; } = 32768;
    public FilePath PromptFile { get; set; } = "translate.md";
    public string RealtimeOpenAIModel { get; set; } = "gpt-5.6-luna";
    public int RealtimeOpenAIModelMaxTokens { get; set; } = 4192;
    public string RealtimeGeminiModel { get; set; } = "";
    public FilePath RealtimePromptFile { get; set; } = "translate-realtime.md";
    public string UITextOpenAIModel { get; set; } = "gpt-5.6-terra";
    public FilePath UITextPromptFile { get; set; } = "translate-ui-text.md";
    public int ContentMinLengthWithoutContext { get; set; } = 150;
    public int ContextMessageCount { get; set; } = 5;
    public int StreamingMinContentLength { get; set; } = 50;
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan HangingTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);
}

public class LanguageDetectionSettings
{
    public string OpenAIModel { get; set; } = "gpt-5.6-luna";
    public FilePath PromptFile { get; set; } = "detect-languages.md";
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromMinutes(3);
}

public class SummarizationSettings
{
    public string OpenAIModel { get; set; } = "gpt-5.6-terra";
    public int MinConversationWords { get; set; } = 1200;
    public int MinConversationEntries { get; set; } = 10;
    public int MinLiveConversationWords { get; set; } = 150;
    public int MinLiveConversationEntries { get; set; } = 3;
    public TimeSpan FirstLiveSummaryDelay { get; set; } = TimeSpan.FromMinutes(1);
    // Live summaries are provisional - each pass rewrites the last - so they gate tighter than the one-shot pair below.
    public TimeSpan LiveSummaryMaturity { get; set; } = TimeSpan.FromSeconds(45);
    public TimeSpan LiveResummarizationDelay { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan ChatEntrySummarizationDelay { get; set; } = TimeSpan.FromMinutes(3);
    public TimeSpan ResummarizationDelay { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan ChatEntrySummarizationDelayQuanta { get; set; } = TimeSpan.FromMinutes(1);
    public FilePath SummarizeConversationPromptFile { get; set; } = "summarize-conversation.md";
    public FilePath SummarizeChatDigestPromptFile { get; set; } = "summarize-chat-digest.md";
    public FilePath SuggestChatThreadTitlePromptFile { get; set; } = "suggest-thread-title.md";
    public FilePath SuggestImageDescriptionPromptFile { get; set; } = "suggest-image-description.md";
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public bool IsExpandedByDefault(int words, int entryCount)
        // Tier 2 (below the full-summary gate) materializes expanded; tier 3 (at/above it) collapsed.
        // Lives next to its thresholds so the summary flow and the call path can't drift apart.
        => words < MinConversationWords || entryCount < MinConversationEntries;
}

public class CoachSettings
{
    public bool IsEnabled { get; set; }
    public string OpenAIModel { get; set; } = "gpt-5.6-luna";
    public FilePath PromptFile { get; set; } = "coach-tag-speech.md";
    public int PromptVersion { get; set; } = 1;
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public double MinPauseSeconds { get; set; } = 1;
    public double MaxResponseGapSeconds { get; set; } = 10;
    public int BatchChunkWords { get; set; } = 2000;
    public TimeSpan ConversationMaturity { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan MaxConversationWait { get; set; } = TimeSpan.FromHours(2);
    public int MaxTaggerCallsPerUserPerDay { get; set; } = 500;
    public int MaxRunEntries { get; set; } = 400;
    public int MaxTaggerCallsPerRun { get; set; } = 50;
}
