namespace ActualChat.Chat.Coach;

// Set into Operation.Items in the write phase of AnalyzeConversation and read back in its
// invalidation phase, possibly on another node via _Operations.ItemsJson (Newtonsoft), hence
// the explicit serialization attributes.
[DataContract, MessagePackObject(AllowPrivate = true)]
internal sealed partial record CoachRunTouch(
    [property: DataMember(Order = 0), Key(0)] ConversationId Id,
    [property: DataMember(Order = 1), Key(1)] ApiArray<AuthorId> AuthorIds,
    [property: DataMember(Order = 2), Key(2)] ApiArray<CoachTaggedEntry> TaggedEntries);

[DataContract, MessagePackObject(AllowPrivate = true)]
internal sealed partial record CoachTaggedEntry(
    [property: DataMember(Order = 0), Key(0)] ChatEntryId Id,
    [property: DataMember(Order = 1), Key(1)] AuthorId AuthorId);

// The rows DeleteUserData removed, for its invalidation phase
[DataContract, MessagePackObject(AllowPrivate = true)]
internal sealed partial record CoachUserDataTouch(
    [property: DataMember(Order = 0), Key(0)] ApiArray<CoachTaggedEntry> Entries,
    [property: DataMember(Order = 1), Key(1)] ApiArray<CoachRunTouch> Runs);
