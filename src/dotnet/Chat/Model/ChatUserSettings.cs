namespace ActualChat.Chat;

public record ChatUserSettings
{
    public Symbol ChatId { get; init; }
    public Symbol UserId { get; init; }
    public long Version { get; init; }
    public LanguageId Language { get; init; }
}
