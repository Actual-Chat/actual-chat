namespace ActualChat.MLSearch.Documents;

[StructLayout(LayoutKind.Auto)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor]
public readonly record struct ChatSliceEntry(
    TextEntryId Id, // NOTE(AY): JSON representation of XxxId is still a string
    long LocalId,
    long Version);
