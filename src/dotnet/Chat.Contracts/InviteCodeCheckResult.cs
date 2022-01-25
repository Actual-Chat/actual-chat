namespace ActualChat.Chat;

public record InviteCodeCheckResult
{
    public bool IsValid { get; init; }
    public string ChatTitle { get; init; } = "";
}
