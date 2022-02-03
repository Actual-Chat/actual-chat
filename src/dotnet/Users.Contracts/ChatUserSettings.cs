namespace ActualChat.Users;

public record ChatUserSettings
{
    public long Version { get; init; }
    public LanguageId Language { get; init; }
    public Symbol AvatarId { get; init; }
}
