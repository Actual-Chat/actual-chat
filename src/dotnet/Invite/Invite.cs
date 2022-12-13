using Stl.Versioning;

#pragma warning disable MA0049 // Allows ActualChat.Invite.Invite
namespace ActualChat.Invite;

[DataContract]
public sealed record Invite(
    [property: DataMember] Symbol Id,
    [property: DataMember] long Version = 0
    ) : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember] public Symbol CreatedBy { get; init; } = "";
    [DataMember] public Moment CreatedAt { get; init; }
    [DataMember] public Moment ExpiresOn { get; init; }
    [DataMember] public int Remaining { get; init; }
    [DataMember] public InviteDetails Details { get; init; } = null!;

    public Invite() : this(Symbol.Empty) { }

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

public sealed record InviteDetails : IRequirementTarget
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

public record ChatInviteDetails(ChatId ChatId);

public record UserInviteDetails;
