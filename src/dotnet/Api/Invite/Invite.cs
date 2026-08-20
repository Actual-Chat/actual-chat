using ActualLab.Rpc;
using ActualLab.Versioning;

namespace ActualChat.Invite;

#pragma warning disable MA0049 // Allows ActualChat.Invite.Invite

/// <summary>
/// Abstract invitation link. Concrete kinds: <see cref="ChatInvite"/>,
/// <see cref="PlaceInvite"/>, <see cref="UserInvite"/>.
/// </summary>
[RpcSerializable]
[DataContract, MessagePackObject]
[Union(0, typeof(ChatInvite))]
[Union(1, typeof(PlaceInvite))]
[Union(2, typeof(UserInvite))]
public abstract partial record Invite(
    [property: DataMember, Key(0)] Symbol Id,
    [property: DataMember, Key(1)] long Version = 0
    ) : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, Key(2)] public string CreatedBy { get; init; } = "";
    [DataMember, Key(3)] public Moment CreatedAt { get; init; }
    [DataMember, Key(4)] public Moment ExpiresOn { get; init; }
    [DataMember, Key(5)] public int Remaining { get; init; }

    public abstract string GetSearchKey();

    // True when redeeming this invite is what could have granted access to chatId -
    // either the invite's own chat, or the root chat of its place.
    public abstract bool Grants(ChatId chatId);

    public bool CanUse(int useCount = 1)
        => CanUse(Moment.Now, useCount);

    public bool CanUse(Moment now, int useCount = 1)
        => Remaining >= useCount && (ExpiresOn == default || ExpiresOn > now);

    public Invite Use(VersionGenerator<long> versionGenerator, int useCount = 1)
        => Use(versionGenerator, Moment.Now, useCount);

    public Invite Use(VersionGenerator<long> versionGenerator, Moment now, int useCount = 1)
    {
        if (!CanUse(now, useCount))
            throw StandardError.Unauthorized("The invite link is already used.");

        return this with {
            Version = versionGenerator.NextVersion(Version),
            Remaining = Remaining - useCount,
        };
    }

    public Invite Revoke(VersionGenerator<long> versionGenerator)
    {
        if (Remaining <= 0)
            throw StandardError.Constraint("The invite link is no active already.");

        return this with {
            Version = versionGenerator.NextVersion(Version),
            Remaining = 0,
        };
    }

    public Invite Mask()
        => this with {
            CreatedBy = "",
            ExpiresOn = default,
            Remaining = 0,
        };
}
