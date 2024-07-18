namespace ActualChat.MLSearch.Documents;

[StructLayout(LayoutKind.Auto)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor]
public readonly record struct ChatSliceEntry(ChatEntryId Id, long LocalId, long Version);
