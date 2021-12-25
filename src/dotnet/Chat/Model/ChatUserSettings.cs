namespace ActualChat.Chat;

public record ChatUserSettings
{
    public long Version { get; init; }
    public LanguageId Language { get; init; }
}
