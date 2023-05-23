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
            CreatedBy = Symbol.Empty,
            ExpiresOn = Moment.EpochStart,
            Remaining = 0,
        };
}

[DataContract]
public sealed record InviteDetails : IUnionRecord<InviteDetailsOption?>
{
    // Union options
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public InviteDetailsOption? Option { get; init; }

    [DataMember]
    public ChatInviteOption? Chat {
        get => Option as ChatInviteOption;
        init => Option ??= value;
    }

    [DataMember]
    public UserInviteOption? User {
        get => Option as UserInviteOption;
        init => Option ??= value;
    }

    public string GetSearchKey()
        => Option.Require().GetSearchKey();

    public static implicit operator InviteDetails(InviteDetailsOption option) => new() { Option = option };
}

public abstract record InviteDetailsOption : IRequirementTarget
{
    public abstract string GetSearchKey();
}

[DataContract]
public record ChatInviteOption(
    [property: DataMember] ChatId ChatId
    ) : InviteDetailsOption
{
    public override string GetSearchKey()
        => $"{nameof(ChatInviteOption)}:{ChatId}";
}

[DataContract]
public record UserInviteOption : InviteDetailsOption
{
    public override string GetSearchKey()
        => nameof(UserInviteOption);
}
