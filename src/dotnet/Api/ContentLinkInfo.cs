namespace ActualChat;

/// <summary>
/// Metadata for a content link including title, picture, and description.
/// </summary>
[DataContract, MessagePackObject]
public partial record ContentLinkInfo(
    [property: DataMember, Key(0)] ContentId Id,
    [property: DataMember, Key(1)] string Title,
    [property: DataMember, Key(2)] Picture? Picture,
    [property: DataMember, Key(3)] string Description)
{
    public static ContentLinkInfo RemovedOrUnknown(ContentId id)
        => new (id, "Removed or Unknown", null, "");
}
