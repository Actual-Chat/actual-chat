using ActualLab.IO;

namespace ActualChat.Chat.Module;

public sealed class ChatSettings
{
    public string OpenAIChatModel { get; set; } = "o4-mini";
    public string OpenAIApiKey { get; set; } = "";
    public string OpenAIProxy { get; set; } = "";
    public bool IsTranslationEnabled { get; set; }
    public TranslationSettings Translation { get; set; } = new ();
    public LanguageDetectionSettings LanguageDetection { get; set; } = new ();
    public bool IsSummarizationEnabled => !OpenAIApiKey.IsNullOrEmpty() && !OpenAIChatModel.IsNullOrEmpty();
    public TimeSpan ChatEntrySummarizationDelay { get; set; } = TimeSpan.FromMinutes(2);
    public int MinConversationWords { get; set; } = 400;
    public int MinConversationEntries { get; set; } = 3;
}

public class TranslationSettings
{
    public FilePath PromptFile { get; set; } = "";
    public int ContextMessageCount { get; set; } = 10;
    public string ContextSeparator { get; set; } = "⚋⚊⚋⚊⚋⚊⚋⚊⚋⚊⚋⚊⚋⚊⚋⚊⚋⚊⚋⚊⚋⚊⚋⚊";
    public string OpenAIKey { get; set; } = "";
    public string OpenAIModel { get; set; } = "gpt-4.1";
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

public class LanguageDetectionSettings
{
    public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(0.5);
    public int ParallelismDegree { get; set; } = 10;
    public int FlowBatchSize { get; set; } = 200;
    public int FlowQuota { get; set; } = 1000;
    public TimeSpan FlowBatchTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int OpenAIRequestTokenLimit { get; set; } = 1000;
    public int EntryContentTruncationLength { get; set; } = 100;
    public FilePath PromptFile { get; set; } = "";
    public string OpenAIKey { get; set; } = "";
    public string OpenAIModel { get; set; } = "gpt-4.1-mini";
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromMinutes(3);
}
