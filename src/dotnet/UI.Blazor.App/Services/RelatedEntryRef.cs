namespace ActualChat.UI.Blazor.App.Services;

public enum RelatedEntryKind
{
    Reply,
    Edit,
}

[DataContract, MessagePackObject]
public sealed partial record RelatedEntryRef(
    [property: DataMember, Key(0)] RelatedEntryKind Kind,
    [property: DataMember, Key(1)] EntryRef EntryRef)
{
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatEntryId EntryId => EntryRef.EntryId;
}

[DataContract, MessagePackObject]
public sealed partial record EntryRef([property: DataMember, Key(0)] ChatEntryId EntryId)
{
    [DataMember, Key(1)]
    public ChatEntry? ChatEntry { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ChatId ChatId => EntryId.ChatId;
}
