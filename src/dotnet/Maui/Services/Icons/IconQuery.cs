using ActualChat.UI;

namespace ActualChat.Maui.Services;

public record IconQuery(Picture? Picture, AvatarKind AvatarKind, string DefaultAvatarKey, int? AvatarSize = null)
{
    public string AvatarKey => Picture?.AvatarKey.NullIfEmpty() ?? DefaultAvatarKey;
}
