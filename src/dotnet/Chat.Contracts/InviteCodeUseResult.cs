namespace ActualChat.Chat;

public record InviteCodeUseResult
{
    public bool IsValid { get; init; }
    public string ChatId { get; init; } = "";
}
