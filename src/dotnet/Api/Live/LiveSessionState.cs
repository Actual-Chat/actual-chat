
namespace ActualChat.Live;

/// <summary>
/// The persisted per-chat live-session state: call/lifecycle facts (host, rules, the 2+ peer start,
/// closing) plus the open <see cref="LiveConversation"/>, which exists once the session starts.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record LiveSessionState
{
    [DataMember(Order = 0), Key(0)]
    public ChatId ChatId { get; init; } = null!;
    [DataMember(Order = 1), Key(1)]
    public long FirstSpeechLid { get; init; }
    [DataMember(Order = 2), Key(2)]
    public Moment FirstSpeechAt { get; init; }
    // Everyone who registered a stream in the session, in order of arrival
    [DataMember(Order = 3), Key(3)]
    public IReadOnlyList<AuthorId> AuthorIds { get; init; } = [];
    [DataMember(Order = 4), Key(4)]
    public bool IsTranscriptionOn { get; init; }
    [DataMember(Order = 5), Key(5)]
    public bool StartNotificationSent { get; init; }
    [DataMember(Order = 6), Key(6)]
    public long Version { get; init; }
    // Nobody is streaming audio or video (a connected call aside). It is the server's own liveness timer:
    // the session stays open and ends for good once QuietSince is older than the grace period.
    [DataMember(Order = 8), Key(8)]
    public Moment? QuietSince { get; init; }
    [DataMember(Order = 9), Key(9)]
    public AuthorId HostAuthorId { get; init; } = null!;
    [DataMember(Order = 10), Key(10)]
    public SessionRules Rules { get; init; } = SessionRules.Default;
    // What started the session, never changed after: a call placed into an ambient session leaves it
    // Ambient. Whether a call is in the session now is the chat's LiveCall, not this.
    [DataMember(Order = 11), Key(11)]
    public LiveSessionKind Kind { get; init; } = LiveSessionKind.Ambient;
    // Set once when an answer starts the session and never moved, unlike Host, which ReassignHost hands
    // to another participant; it outlives the LiveCall, which a group call's session does too.
    [DataMember(Order = 12), Key(12)]
    public AuthorId? CallerId { get; init; }
    // Set once any stream in the session is transcribed, and never cleared. Not IsTranscriptionOn: that one is
    // the chat's summarization setting, so a call or an unsummarized chat lacks it with every word transcribed.
    [DataMember(Order = 13), Key(13)]
    public bool HasTranscript { get; init; }
    // Null until the session starts
    [DataMember(Order = 14), Key(14)]
    public LiveConversation? OpenConversation { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsStarted => OpenConversation is not null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public Moment? StartedAt => OpenConversation?.StartedAt;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsCall => Kind == LiveSessionKind.Call;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsQuiet => QuietSince is not null;
}
