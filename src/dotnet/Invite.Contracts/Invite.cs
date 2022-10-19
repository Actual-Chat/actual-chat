using Stl.Versioning;

#pragma warning disable MA0049 // Allows ActualChat.Invite.Invite
namespace ActualChat.Invite;

public sealed record Invite : IHasId<Symbol>, IRequirementTarget
{
    public Symbol Id { get; init; } = "";
    public long Version { get; init; }

    public Symbol CreatedBy { get; init; } = "";
    public Moment CreatedAt { get; init; }
    public Moment ExpiresOn { get; init; }
    public int Remaining { get; init; }
    public InviteDetails Details { get; init; } = null!;

    public Invite Use(VersionGenerator<long> versionGenerator, int useCount = 1)
    {
        if (Remaining < useCount)
            throw StandardError.Unauthorized("The invite link is already used.");
        return this with {
            Version = versionGenerator.NextVersion(Version),
            Remaining = Remaining - useCount,
        };
    }

    public Invite Mask()
        => this with {
            CreatedBy = Symbol.Empty,
            ExpiresOn = Moment.EpochStart,
            Remaining = 0,
        };
}

public sealed record InviteDetails
{
    public ChatInviteDetails? Chat { get; init; }
    public UserInviteDetails? User { get; init; }

    public string GetSearchKey()
    {
        if (Chat is { } chat)
            return $"{nameof(ChatInviteDetails)}:{chat.ChatId}";
        if (User is { } user)
            return $"{nameof(UserInviteDetails)}";
        return "-";
    }
}

public record ChatInviteDetails(Symbol ChatId);

public record UserInviteDetails;
