namespace ActualChat.Chat.Module;

public sealed class ChatSettings
{
    public string OpenAIChatModel { get; set; } = "";
    public string OpenAIApiKey { get; set; } = "";
    public string OpenAIProxy { get; set; } = "";
    public TimeSpan EntryLanguageDetectionTimeout { get; set; } = TimeSpan.FromSeconds(3);
    public string DetectAllLanguagesPrompt { get; set; } = "";
    public string DetectSingleLanguagePrompt { get; set; } = "";
    public string TranslatePromptFormat { get; set; } = "";
    public bool IsTranslationEnabled { get; set; }
}
