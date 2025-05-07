namespace ActualChat.MLSearch.Documents;

[StructLayout(LayoutKind.Auto)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor]
public readonly record struct ChatSliceEntry(
    // TODO(AY): Serialization(Id) + maybe replace to string?
    TextEntryId Id,
    long LocalId,
    long Version);
