namespace ActualChat.Invite;

public record InviteUsageResult(bool Succeeded, string? Message, Invite? Invite)
{
    public static InviteUsageResult Success(Invite invite) => new InviteUsageResult(true, null, invite);
    public static InviteUsageResult Fail(string error) => new InviteUsageResult(false, error, null);
}
