#pragma warning disable MA0049 // Allows ActualChat.Invite.Invite
namespace ActualChat.Invite;

public record Invite
{
    public Symbol Id { get; init; } = "";
    public long Version { get; init; }
    public Symbol CreatedBy { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresOn { get; init; }
    public string Code { get; init; } = "";
    public int Remaining { get; set; }
    public InviteDetailsDiscriminator? Details { get; set; }
}
