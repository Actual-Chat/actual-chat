namespace ActualChat.Audio;

[DataContract, MessagePackObject]
public sealed partial record SpeechPaceAnalysis(
    [property: DataMember, Key(0)] SpeechPaceSegment[] Segments,
    [property: DataMember, Key(1)] int ValidWords,
    [property: DataMember, Key(2)] int RejectedWords,
    [property: DataMember, Key(3)] int UnclassifiedWords,
    [property: DataMember, Key(4)] int UnclassifiedMilliseconds,
    [property: DataMember, Key(5)] int PauseMilliseconds,
    [property: DataMember, Key(6)] int UnmappedMilliseconds);
