namespace ActualChat.Chat;

/// <summary>
/// A voice entry being pushed in call by call: the handle to send more, and how much text and
/// audio the server has accepted. <see cref="EntryId"/> is null throughout: the audio pipeline
/// creates the entry without surfacing its id, so a producer finds its message by listing.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ChatVoiceStream(
    [property: DataMember(Order = 0), Key(0)] StreamId Id,
    [property: DataMember(Order = 1), Key(1)] ChatEntryId? EntryId,
    [property: DataMember(Order = 2), Key(2)] int TextOffset,
    [property: DataMember(Order = 3), Key(3)] long AudioBytes,
    [property: DataMember(Order = 4), Key(4)] bool IsCompleted);
