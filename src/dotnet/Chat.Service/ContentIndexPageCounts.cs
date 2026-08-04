using MessagePack;

namespace ActualChat.Chat;

// Per-month page-count snapshot of an affected content index, set during a
// command's write phase via Operation.Items.KeylessSet and read back during
// its invalidation phase (possibly on another node — Operation.Items round-
// trips through _Operations.ItemsJson). Hence the explicit serialization
// attributes: Newtonsoft via DbOperation.Serializer is what actually drives
// cross-node delivery; MessagePack is kept aligned with the codebase's
// standard for round-trippable wire types.
[DataContract, MessagePackObject(AllowPrivate = true)]
internal sealed partial record ContentIndexPageCounts(
    [property: DataMember(Order = 0), Key(0)] Dictionary<string, int> PageCounts);
