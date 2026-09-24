namespace ActualChat.Transcription;

/// <summary>
/// One incremental transcript update from an external producer. <see cref="AudioOffset"/> is a
/// position in the producer's own audio, never a timestamp; null lets the server derive it.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record ExternalTranscriptChunk(
    [property: DataMember(Order = 0), Key(0)] string Text,
    [property: DataMember(Order = 1), Key(1)] bool IsAppend,
    [property: DataMember(Order = 2), Key(2)] double? AudioOffset,
    [property: DataMember(Order = 3), Key(3)] bool IsStable);
