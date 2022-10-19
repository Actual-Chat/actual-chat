namespace ActualChat.Users;

public record ChatUserSettings
{
    public LanguageId Language { get; init; }
    public Symbol AvatarId { get; init; }
}
