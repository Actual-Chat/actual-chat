namespace ActualChat.Chat;

/// <summary>
/// A voice entry being pushed in call by call: the handle to send more, and how much text and
/// audio the server has accepted. <see cref="AudioDuration"/> is what the server decoded, not
/// what it was sent - a chunk it could not read adds bytes but no time.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ChatVoiceStream(
    [property: DataMember(Order = 0), Key(0)] StreamId Id,
    [property: DataMember(Order = 1), Key(1)] ChatEntryId? EntryId,
    [property: DataMember(Order = 2), Key(2)] int TextOffset,
    [property: DataMember(Order = 3), Key(3)] long AudioBytes,
    [property: DataMember(Order = 4), Key(4)] bool IsCompleted,
    [property: DataMember(Order = 5), Key(5)] TimeSpan AudioDuration);
