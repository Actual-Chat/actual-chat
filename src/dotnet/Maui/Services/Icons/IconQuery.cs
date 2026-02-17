using ActualChat.UI;

namespace ActualChat.Maui.Services;

public record IconQuery(Picture? Picture, AvatarKind AvatarKind, string DefaultAvatarKey, int? AvatarSize = null, string AvatarTitle = "")
{
    public string AvatarKey => Picture?.AvatarKey.NullIfEmpty() ?? DefaultAvatarKey;
}
