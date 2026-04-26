namespace ActualChat.Invite;

/// <summary>
/// Wire-frozen v2.7 <see cref="InviteDetails"/> wrapper used inside <see cref="LegacyInvite"/>.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record LegacyInviteDetails : IUnionRecord<LegacyInviteDetailsOption?>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public LegacyInviteDetailsOption? Option { get; init; }

    [DataMember, MemoryPackOrder(0)]
    public LegacyChatInviteOption? Chat {
        get => Option as LegacyChatInviteOption;
        init => Option ??= value;
    }

    [DataMember, MemoryPackOrder(1)]
    public LegacyUserInviteOption? User {
        get => Option as LegacyUserInviteOption;
        init => Option ??= value;
    }

    [DataMember, MemoryPackOrder(2)]
    public LegacyPlaceInviteOption? Place {
        get => Option as LegacyPlaceInviteOption;
        init => Option ??= value;
    }

    public static implicit operator LegacyInviteDetails(LegacyInviteDetailsOption option)
        => new() { Option = option };
}

public abstract record LegacyInviteDetailsOption;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record LegacyChatInviteOption(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId
    ) : LegacyInviteDetailsOption;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record LegacyPlaceInviteOption(
    [property: DataMember, MemoryPackOrder(0)] PlaceId PlaceId
    ) : LegacyInviteDetailsOption;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record LegacyUserInviteOption : LegacyInviteDetailsOption;
