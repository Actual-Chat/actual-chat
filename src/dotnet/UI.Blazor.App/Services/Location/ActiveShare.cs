namespace ActualChat.UI.Blazor.App.Services;

[DataContract]
[MessagePackObject]
public sealed partial record ActiveShare(
    [property: DataMember, Key(0)] ChatId ChatId,
    // null until the first fix creates the share and posts the entry
    [property: DataMember, Key(1)] SharedLocationId? LocationId,
    // Wall-clock (ServerClock) so it survives restarts
    [property: DataMember, Key(2)] Moment StartedAt,
    // The exact Constants.Location.Durations value the user picked — the server accepts only these
    [property: DataMember, Key(3)] TimeSpan Duration
)
{
    // StartedAt + TimeSpan.MaxValue would silently wrap negative
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Moment ExpiresAt => Duration == Constants.Location.UnlimitedDuration
        ? Moment.MaxValue
        : StartedAt + Duration;
}
