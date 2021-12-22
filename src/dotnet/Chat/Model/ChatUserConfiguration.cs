namespace ActualChat.Chat;

public record ChatUserConfiguration
{
    public Symbol Id { get; init; } = Symbol.Empty;
    public Symbol ChatId { get; init; }
    public Symbol UserId { get; init; }
    public ImmutableOptionSet Options { get; init; } = ImmutableOptionSet.Empty;
}
