namespace ActualChat;

public record ContentId(ContentKind Kind, StringIdentifier Id);

public enum ContentKind
{
    User,
    Chat,
    Author,
    Place
}

public record ContentLinkInfo(ContentId Id, string Title, Picture? Picture, string Description)
{
    public static ContentLinkInfo RemovedOrUnknown(ContentId id)
        => new ContentLinkInfo(id, "Removed or Unknown", null, "");
}
