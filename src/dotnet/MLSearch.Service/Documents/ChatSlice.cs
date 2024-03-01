namespace ActualChat.MLSearch.Documents;

internal record ChatSlice(ChatSliceMetadata Metadata, string Text)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public string Id => Metadata.ChatEntries.IsDefaultOrEmpty
        ? string.Empty
        : $"{Metadata.ChatEntries[0]}:{(Metadata.StartOffset ?? 0).ToString(CultureInfo.InvariantCulture)}";
}
