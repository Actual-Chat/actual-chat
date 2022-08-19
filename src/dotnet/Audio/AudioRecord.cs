namespace ActualChat.Audio;

[DataContract]
public record AudioRecord(
        [property: DataMember] string Id, // Ignored on upload
        [property: DataMember] string SessionId,
        [property: DataMember] string ChatId,
        [property: DataMember] double ClientStartOffset)
    : IHasId<string>
{
    private static string NewId() => Ulid.NewUlid().ToString();

    private Session? _session;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Session Session {
        get {
            // Returns cached Session object w/ the matching Id
            if (_session == null || !OrdinalEquals(_session.Id.Value, SessionId))
                _session = SessionId.IsNullOrEmpty() ? Session.Null : new Session(SessionId);
            return _session;
        }
    }

    public AudioRecord(string sessionId, string chatId,  double clientStartOffset)
        : this(NewId(), sessionId, chatId,  clientStartOffset) { }

    // This record relies on referential equality
    public virtual bool Equals(AudioRecord? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
