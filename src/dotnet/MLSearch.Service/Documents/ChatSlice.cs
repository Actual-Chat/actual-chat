namespace ActualChat.MLSearch.Documents;

public interface IHasText
{
    string Text { get; }
}

public sealed record ChatSlice(ChatSliceMetadata Metadata, string Text): IHasId<string>, IHasText
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Id => Metadata.ChatEntries.IsDefaultOrEmpty
        ? string.Empty
        : FormatId(Metadata.ChatEntries[0].Id, Metadata.StartOffset);

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public long Version => Metadata.ChatEntries.IsDefaultOrEmpty
        ? 0
        : Metadata.ChatEntries.Max(e => e.Version);

    public static string FormatId(in ChatEntryId entryId, int? startOffset)
        => $"{entryId}:{(startOffset ?? 0).ToString(CultureInfo.InvariantCulture)}";
}
