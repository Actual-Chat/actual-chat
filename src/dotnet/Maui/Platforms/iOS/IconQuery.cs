using ActualChat.UI;

namespace ActualChat.Maui;

public record IconQuery(Picture? Picture, AvatarKind AvatarKind, string DefaultAvatarKey)
{
    public string AvatarKey => Picture?.AvatarKey.NullIfEmpty() ?? DefaultAvatarKey;
}
