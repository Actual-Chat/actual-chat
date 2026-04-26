namespace ActualChat.Invite;

/// <summary>
/// Invite link for joining a specific place.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record PlaceInvite : Invite
{
    [DataMember, Key(10)] public PlaceId PlaceId { get; init; }

    public PlaceInvite() : base(Symbol.Empty) { }

    [SerializationConstructor]
    public PlaceInvite(Symbol id, long version = 0) : base(id, version) { }

    public static PlaceInvite New(int remaining, PlaceId placeId)
        => new(Symbol.Empty) { Remaining = remaining, PlaceId = placeId };

    // Keep the v2.7 string ("PlaceInviteOption:...") so existing DbInvite.SearchKey
    // rows stay reachable across the union refactor.
    public static string GetSearchKey(PlaceId placeId)
        => $"PlaceInviteOption:{placeId}";

    public override string GetSearchKey()
        => GetSearchKey(PlaceId);
}
