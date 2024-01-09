using MemoryPack;
using ActualLab.Versioning;

namespace ActualChat.Invite;

#pragma warning disable MA0049 // Allows ActualChat.Invite.Invite

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record Invite(
    [property: DataMember, MemoryPackOrder(0)] Symbol Id,
    [property: DataMember, MemoryPackOrder(1)] long Version = 0
    ) : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2)] public Symbol CreatedBy { get; init; } = "";
    [DataMember, MemoryPackOrder(3)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(4)] public Moment ExpiresOn { get; init; }
    [DataMember, MemoryPackOrder(5)] public int Remaining { get; init; }
    [DataMember, MemoryPackOrder(6)] public InviteDetails Details { get; init; } = null!;

    public static Invite New(int remaining, InviteDetails details)
        => new (Symbol.Empty) {
            Remaining = remaining,
            Details = details,
        };

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
            ExpiresOn = default,
            Remaining = 0,
        };
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record InviteDetails : IUnionRecord<InviteDetailsOption?>
{
    // Union options
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public InviteDetailsOption? Option { get; init; }

    [DataMember, MemoryPackOrder(0)]
    public ChatInviteOption? Chat {
        get => Option as ChatInviteOption;
        init => Option ??= value;
    }

    [DataMember, MemoryPackOrder(1)]
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

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ChatInviteOption(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId
    ) : InviteDetailsOption
{
    public override string GetSearchKey()
        => $"{nameof(ChatInviteOption)}:{ChatId}";
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record UserInviteOption : InviteDetailsOption
{
    public override string GetSearchKey()
        => nameof(UserInviteOption);
}
