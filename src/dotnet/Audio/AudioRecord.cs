using System.Text.Json.Serialization;

namespace ActualChat.Audio;

[DataContract]
public record AudioRecord(
        [property: DataMember(Order = 0)] string Id, // Ignored on upload
        [property: DataMember(Order = 1)] string SessionId,
        [property: DataMember(Order = 2)] string ChatId,
        [property: DataMember(Order = 3)] AudioFormat Format,
        [property: DataMember(Order = 4)] double ClientStartOffset)
    : IHasId<string>
{
    private static string NewId() => Ulid.NewUlid().ToString();

    private Session? _session;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public Session Session {
        get {
            // Returns cached Session object w/ the matching Id
            if (_session == null || !StringComparer.Ordinal.Equals(_session.Id.Value, SessionId))
                _session = SessionId.IsNullOrEmpty() ? Session.Null : new Session(SessionId);
            return _session;
        }
    }

    public AudioRecord() : this("", "", null!, null!, 0) { }
    public AudioRecord(string sessionId, string chatId, AudioFormat format, double clientStartOffset)
        : this(NewId(), sessionId, chatId, format, clientStartOffset) { }

    // This record relies on referential equality
    public virtual bool Equals(AudioRecord? other)
        => ReferenceEquals(this, other);
    public override int GetHashCode()
        => RuntimeHelpers.GetHashCode(this);
}
