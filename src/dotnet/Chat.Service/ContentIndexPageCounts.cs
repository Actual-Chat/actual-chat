namespace ActualChat.Chat;

// Per-month page-count snapshot of an affected content index, computed while the command writes and
// read by its deferred invalidation block. It no longer round-trips through Operation.Items, but it
// stays a round-trippable wire type - that's the codebase standard, and it's what any future event
// payload or compute-method argument carrying it would need.
[DataContract, MessagePackObject(AllowPrivate = true)]
internal sealed partial record ContentIndexPageCounts(
    [property: DataMember(Order = 0), Key(0)] Dictionary<string, int> PageCounts);
