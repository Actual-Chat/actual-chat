using ActualLab.Versioning;

namespace ActualChat.Invite;

/// <summary>
/// Represents an invitation link for joining chats, places, or adding contacts.
/// </summary>
#pragma warning disable MA0049 // Allows ActualChat.Invite.Invite

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record Invite(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Symbol Id,
    [property: DataMember, MemoryPackOrder(1), Key(1)] long Version = 0
    ) : IHasId<Symbol>, IHasVersion<long>, IRequirementTarget
{
    [DataMember, MemoryPackOrder(2), Key(2)] public string CreatedBy { get; init; } = "";
    [DataMember, MemoryPackOrder(3), Key(3)] public Moment CreatedAt { get; init; }
    [DataMember, MemoryPackOrder(4), Key(4)] public Moment ExpiresOn { get; init; }
    [DataMember, MemoryPackOrder(5), Key(5)] public int Remaining { get; init; }
    [DataMember, MemoryPackOrder(6), Key(6)] public InviteDetails Details { get; init; } = null!;

    public static Invite New(int remaining, InviteDetails details)
        => new (Symbol.Empty) {
            Remaining = remaining,
            Details = details,
        };

    public bool CanUse(int useCount = 1)
        => Remaining >= useCount;

    public Invite Use(VersionGenerator<long> versionGenerator, int useCount = 1)
    {
        if (!CanUse(useCount))
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

/// <summary>
/// Contains the target details of an <see cref="Invite"/>.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record InviteDetails : IUnionRecord<InviteDetailsOption?>
{
    // Union options
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public InviteDetailsOption? Option { get; init; }

    [DataMember, MemoryPackOrder(0), Key(0)]
    public ChatInviteOption? Chat {
        get => Option as ChatInviteOption;
        init => Option ??= value;
    }

    [DataMember, MemoryPackOrder(1), Key(1)]
    public UserInviteOption? User {
        get => Option as UserInviteOption;
        init => Option ??= value;
    }

    [DataMember, MemoryPackOrder(2), Key(2)]
    public PlaceInviteOption? Place {
        get => Option as PlaceInviteOption;
        init => Option ??= value;
    }

    public string GetSearchKey()
        => Option.Require().GetSearchKey();

    public static implicit operator InviteDetails(InviteDetailsOption option) => new() { Option = option };
}

/// <summary>
/// Base class for invite detail options.
/// </summary>
public abstract record InviteDetailsOption : IRequirementTarget
{
    public abstract string GetSearchKey();
}

/// <summary>
/// Invite option for joining a chat.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record ChatInviteOption(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId ChatId
    ) : InviteDetailsOption
{
    public override string GetSearchKey()
        => $"{nameof(ChatInviteOption)}:{ChatId}";
}

/// <summary>
/// Invite option for joining a place.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record PlaceInviteOption(
    [property: DataMember, MemoryPackOrder(0), Key(0)] PlaceId PlaceId
) : InviteDetailsOption
{
    public override string GetSearchKey()
        => $"{nameof(PlaceInviteOption)}:{PlaceId}";
}

/// <summary>
/// Invite option for adding a user as a contact.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record UserInviteOption : InviteDetailsOption
{
    public override string GetSearchKey()
        => nameof(UserInviteOption);
}
