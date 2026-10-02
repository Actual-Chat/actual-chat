using ActualChat.Chat;

namespace ActualChat.Streaming;

/// <summary>
/// The call a chat is in, from the first ring to its end. Until it is answered the call exists only
/// here; the answer creates the chat's live session or joins the one already there.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record LiveCall
{
    [DataMember(Order = 0), Key(0)]
    public CallId Id { get; init; } = null!;
    [DataMember(Order = 1), Key(1)]
    public AuthorId CallerId { get; init; } = null!;
    [DataMember(Order = 2), Key(2)]
    public bool HasVideo { get; init; }
    [DataMember(Order = 3), Key(3)]
    public Moment StartedAt { get; init; }
    [DataMember(Order = 4), Key(4)]
    public Moment? AnsweredAt { get; init; }
    // How an unanswered call ended. First writer wins, and an answer counts as a writer: once
    // AnsweredAt is set nothing records an outcome, and once an outcome is set nothing answers.
    [DataMember(Order = 5), Key(5)]
    public CallOutcome Outcome { get; init; }
    [DataMember(Order = 6), Key(6)]
    public long Version { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ChatId => Id.ChatId;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsAnswered => AnsweredAt is not null;
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsResolved => IsAnswered || Outcome != CallOutcome.None;
}
