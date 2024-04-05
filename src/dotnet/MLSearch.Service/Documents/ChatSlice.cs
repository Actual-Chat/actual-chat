namespace ActualChat.MLSearch.Documents;

internal sealed record ChatSlice(ChatSliceMetadata Metadata, string Text): IHasDocId
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Id => Metadata.ChatEntries.IsDefaultOrEmpty
        ? string.Empty
        : FormatId(Metadata.ChatEntries[0].Id, Metadata.StartOffset);

    public static string FormatId(in ChatEntryId entryId, int? startOffset)
        => $"{entryId}:{(startOffset ?? 0).ToString(CultureInfo.InvariantCulture)}";
}
