namespace ActualChat.Chat.Coach;

// What a run touched, for its deferred invalidation block to walk. These no longer round-trip
// through Operation.Items, but they stay round-trippable wire types - that's the codebase standard,
// and it's what any future event payload carrying them would need.
[DataContract, MessagePackObject(AllowPrivate = true)]
internal sealed partial record CoachRunTouch(
    [property: DataMember(Order = 0), Key(0)] ConversationId Id,
    [property: DataMember(Order = 1), Key(1)] ApiArray<AuthorId> AuthorIds,
    [property: DataMember(Order = 2), Key(2)] ApiArray<CoachTaggedEntry> TaggedEntries);

[DataContract, MessagePackObject(AllowPrivate = true)]
internal sealed partial record CoachTaggedEntry(
    [property: DataMember(Order = 0), Key(0)] ChatEntryId Id,
    [property: DataMember(Order = 1), Key(1)] AuthorId AuthorId);
