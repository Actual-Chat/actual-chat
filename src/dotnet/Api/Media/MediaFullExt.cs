namespace ActualChat.Media;

public static class MediaFullExt
{
    public static Media ToMedia(this MediaFull media)
        => new (media.Id) {
            ContentId = media.ContentId,
            Metadata = Clone(media.Metadata),
        };

    private static PropertyBag Clone(PropertyBag metadata)
    {
        var clone = new PropertyBag();
        clone.SetMany(metadata);
        return clone;
    }
}
