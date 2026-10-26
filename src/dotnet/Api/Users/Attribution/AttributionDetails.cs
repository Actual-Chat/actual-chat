namespace ActualChat.Users;

/// <summary>
/// What an attribution says about the ad or link that brought the user, as parsed by <see cref="AttributionParser"/>.
/// Apple reports numeric ids only, so <see cref="KeywordId"/> is set for Apple and <see cref="Term"/> for Google.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record AttributionDetails
{
    [DataMember, Key(0)] public string Platform { get; init; } = "";
    [DataMember, Key(1)] public string Source { get; init; } = "";
    [DataMember, Key(2)] public string Medium { get; init; } = "";
    [DataMember, Key(3)] public string Campaign { get; init; } = "";
    [DataMember, Key(4)] public string AdGroup { get; init; } = "";
    [DataMember, Key(5)] public string Term { get; init; } = "";
    [DataMember, Key(6)] public string KeywordId { get; init; } = "";
    [DataMember, Key(7)] public string MatchType { get; init; } = "";
    [DataMember, Key(8)] public string AdId { get; init; } = "";
    [DataMember, Key(9)] public ClickIdKind? ClickIdKind { get; init; }
    [DataMember, Key(10)] public string ClickId { get; init; } = "";
    [DataMember, Key(11)] public string Claim { get; init; } = "";
    [DataMember, Key(12)] public string InstallKind { get; init; } = "";
    [DataMember, Key(13)] public string Country { get; init; } = "";

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsAttributed => !Campaign.IsNullOrEmpty() || !ClickId.IsNullOrEmpty();

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public string DedupeKey => $"{Platform}|{Campaign}|{AdGroup}|{(KeywordId.IsNullOrEmpty() ? Term : KeywordId)}";
}
