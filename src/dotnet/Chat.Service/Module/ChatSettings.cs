using ActualLab.IO;

namespace ActualChat.Chat.Module;

public sealed class ChatSettings
{
    public string OpenAIChatModel { get; set; } = "";
    public string OpenAIApiKey { get; set; } = "";
    public string OpenAIProxy { get; set; } = "";
    public TimeSpan TranslatorHttpClientTimeout { get; set; } = TimeSpan.FromMinutes(3);
    public TimeSpan BulkLanguageDetectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan LanguageDetectionDelay { get; set; } = TimeSpan.FromSeconds(0.5);
    public int LanguageDetectionBatchSize { get; set; } = 30;
    public int LanguageDetectionQuota { get; set; } = 150;
    public int LanguageDetectionBatchMaxTokenLength { get; set; } = 10000;
    public int LanguageDetectionEntryContentLimit { get; set; } = 200;
    public FilePath DetectLanguagesPromptFile { get; set; } = "";
    public FilePath TranslatePromptFile { get; set; } = "";
    public bool IsTranslationEnabled { get; set; }
    public int TranslationContextMessageCount { get; set; } = 10;
    public string TranslationContextSeparator { get; set; } = "⚋⚊⚋⚊⚋⚊⚋⚊⚋⚊⚋⚊⚋⚊⚋⚊⚋⚊⚋⚊⚋⚊⚋⚊";

    public bool IsSummarizationEnabled => !OpenAIApiKey.IsNullOrEmpty() && !OpenAIChatModel.IsNullOrEmpty();
    public TimeSpan ChatEntrySummarizationDelay { get; set; } = TimeSpan.FromMinutes(2);
    public int MinConversationWords { get; set; } = 400;
    public int MinConversationEntries { get; set; } = 3;
}
