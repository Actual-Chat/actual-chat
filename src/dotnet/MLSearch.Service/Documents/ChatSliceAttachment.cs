
namespace ActualChat.MLSearch.Documents;

[StructLayout(LayoutKind.Auto)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor]
internal readonly record struct ChatSliceAttachment(MediaId Id, string Summary);
