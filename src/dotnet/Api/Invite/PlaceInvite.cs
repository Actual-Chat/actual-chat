namespace ActualChat.Invite;

/// <summary>
/// Invite link for joining a specific place.
/// </summary>
[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record PlaceInvite(Symbol Id, long Version = 0) : Invite(Id, Version)
{
    [DataMember, Key(10)] public PlaceId PlaceId { get; init; } = null!;
    public static PlaceInvite New(int remaining, PlaceId placeId)
        => new(Symbol.Empty) { Remaining = remaining, PlaceId = placeId };
    public PlaceInvite() : this(Symbol.Empty) { }

    public static string GetSearchKey(PlaceId placeId)
        // Keep the v2.7 string ("PlaceInviteOption:...") so existing DbInvite.SearchKey
        // rows stay reachable across the union refactor.
        => $"PlaceInviteOption:{placeId}";

    public override string GetSearchKey()
        => GetSearchKey(PlaceId);

    public override bool Grants(ChatId chatId)
        => PlaceId.RootChatId == chatId;
}
