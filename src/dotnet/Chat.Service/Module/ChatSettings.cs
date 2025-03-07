using ActualLab.IO;

namespace ActualChat.Chat.Module;

public sealed class ChatSettings
{
    public string OpenAIChatModel { get; set; } = "";
    public string OpenAIApiKey { get; set; } = "";
    public string OpenAIProxy { get; set; } = "";
    public TimeSpan BulkLanguageDetectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan LanguageDetectionDelay { get; set; } = TimeSpan.FromSeconds(0.5);
    public FilePath DetectLanguagesPromptFile { get; set; } = "";
    public FilePath TranslatePromptFile { get; set; } = "";
    public bool IsTranslationEnabled { get; set; }

    public bool IsSummarizationEnabled => !OpenAIApiKey.IsNullOrEmpty() && !OpenAIChatModel.IsNullOrEmpty();
    public TimeSpan ChatEntrySummarizationDelay { get; set; } = TimeSpan.FromMinutes(2);
    public int MinConversationWords { get; set; } = 400;
    public int MinConversationEntries { get; set; } = 3;
}
