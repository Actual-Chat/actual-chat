namespace ActualChat;

public class ContentId
{
    public ContentKind Kind { get; }
    public StringIdentifier Id { get; }

    private ContentId(ContentKind kind, StringIdentifier id)
    {
        Kind = kind;
        Id = id;
    }

    public static ContentId New(StringIdentifier id)
    {
        var kind = GetKind(id);
        if (kind is null)
            throw new ArgumentOutOfRangeException(nameof(id));
        return new ContentId(kind.Value, id);
    }

    public static bool IsValid(StringIdentifier id)
        => GetKind(id) is not null;

    private static ContentKind? GetKind(StringIdentifier id)
    {
        if (id is ChatId)
            return ContentKind.Chat;
        if (id is UserId)
            return ContentKind.User;
        if (id is TextEntryId)
            return ContentKind.TextEntry;
        if (id is AuthorId)
            return ContentKind.Author;
        if (id is PlaceId)
            return ContentKind.Place;
        return null;
    }
}

public enum ContentKind
{
    User,
    Chat,
    TextEntry,
    Author,
    Place
}

public record ContentLinkInfo(ContentId Id, string Title, Picture? Picture, string Description)
{
    public static ContentLinkInfo RemovedOrUnknown(ContentId id)
        => new (id, "Removed or Unknown", null, "");
}
