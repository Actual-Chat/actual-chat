namespace ActualChat.Chat;

public enum InviteCodeState { Active = 0, Expired = 1, Revoked = 2 }

public record InviteCode
{
    public Symbol Id { get; init; } = "";
    public long Version { get; init; }
    public Symbol ChatId { get; init; } = "";
    public Symbol CreatedBy { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresOn { get; init; }
    public InviteCodeState State { get; init; }
    public string Value { get; init; } = "";
}
