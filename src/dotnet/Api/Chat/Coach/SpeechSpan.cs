namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public sealed partial record SpeechSpan(
    [property: DataMember, Key(0)] SpeechSpanKind Kind,
    [property: DataMember, Key(1)] string Word,
    [property: DataMember, Key(2)] int Start,
    [property: DataMember, Key(3)] int Length,
    [property: DataMember, Key(4)] ApiArray<string> Synonyms
);
