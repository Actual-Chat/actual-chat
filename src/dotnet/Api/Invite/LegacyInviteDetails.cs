namespace ActualChat.Invite;

/// <summary>
/// Wire-frozen v2.7 <see cref="InviteDetails"/> wrapper used inside <see cref="LegacyInvite"/>.
/// </summary>
[DataContract]
public sealed partial record LegacyInviteDetails : IUnionRecord<LegacyInviteDetailsOption?>
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember]
    public LegacyInviteDetailsOption? Option { get; init; }

    [DataMember]
    public LegacyChatInviteOption? Chat {
        get => Option as LegacyChatInviteOption;
        init => Option ??= value;
    }

    [DataMember]
    public LegacyUserInviteOption? User {
        get => Option as LegacyUserInviteOption;
        init => Option ??= value;
    }

    [DataMember]
    public LegacyPlaceInviteOption? Place {
        get => Option as LegacyPlaceInviteOption;
        init => Option ??= value;
    }

    public static implicit operator LegacyInviteDetails(LegacyInviteDetailsOption option)
        => new() { Option = option };

    public static LegacyInviteDetails From(Invite invite) => invite switch {
        ChatInvite chat => new LegacyInviteDetails { Option = new LegacyChatInviteOption(chat.ChatId) },
        PlaceInvite place => new LegacyInviteDetails { Option = new LegacyPlaceInviteOption(place.PlaceId) },
        UserInvite => new LegacyInviteDetails { Option = new LegacyUserInviteOption() },
        _ => throw StandardError.Format<Invite>($"Unknown invite type: {invite.GetType().Name}"),
    };
}

public abstract record LegacyInviteDetailsOption;

[DataContract]
public partial record LegacyChatInviteOption(
    [property: DataMember] ChatId ChatId
    ) : LegacyInviteDetailsOption;

[DataContract]
public partial record LegacyPlaceInviteOption(
    [property: DataMember] PlaceId PlaceId
    ) : LegacyInviteDetailsOption;

[DataContract]
public partial record LegacyUserInviteOption : LegacyInviteDetailsOption;
