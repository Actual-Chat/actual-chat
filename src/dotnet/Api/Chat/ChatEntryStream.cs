namespace ActualChat.Chat;

/// <summary>
/// A text entry whose content is still being pushed in, as its producer sees it: the handle to
/// push more, how much of the text the server has accepted, and how far a listener's synthesized
/// voice is behind it - null when nothing is speaking this entry.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ChatEntryStream(
    [property: DataMember(Order = 0), Key(0)] StreamId Id,
    [property: DataMember(Order = 1), Key(1)] ChatEntryId EntryId,
    [property: DataMember(Order = 2), Key(2)] int Offset,
    [property: DataMember(Order = 3), Key(3)] bool IsCompleted,
    [property: DataMember(Order = 4), Key(4)] TimeSpan? SpeechBacklog = null);
