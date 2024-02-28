
namespace ActualChat.MLSearch;

[StructLayout(LayoutKind.Auto)]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor]
internal readonly struct ChatSliceAttachment(MediaId id, string summary)
{
    public MediaId Id { get; } = id;
    public string Summary { get; } = summary;
}
