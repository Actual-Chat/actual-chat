namespace ActualChat;

/// <summary>
/// Represents an icon query that combines a custom picture with a fallback avatar.
/// </summary>
public record IconQuery(Picture? Picture, AvatarQuery AvatarQuery)
{
    public static IconQuery Create(
        Picture? picture,
        AvatarKind defaultAvatarKind,
        string defaultAvatarKey,
        int? size = null,
        string? title = null)
        => new(picture, new AvatarQuery {
            Kind = defaultAvatarKind,
            Key = picture?.AvatarKey.NullIfEmpty() ?? defaultAvatarKey,
            Format = AvatarFormat.Png,
            Size = size,
            Title = title,
        });
}
