using ActualChat.UI.Blazor.Components;

namespace ActualChat.App.Maui.IosShareExt.Components;

public record IconQuery(Picture? Picture, AvatarKind AvatarKind, string DefaultAvatarKey)
{
    public string AvatarKey => Picture?.AvatarKey.NullIfEmpty() ?? DefaultAvatarKey;
}
