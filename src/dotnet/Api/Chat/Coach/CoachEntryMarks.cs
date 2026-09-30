namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public sealed partial record CoachEntryMarks(
    [property: DataMember, Key(0)] long EntryLid,
    [property: DataMember, Key(1)] ApiArray<SpeechSpan> Spans
);
