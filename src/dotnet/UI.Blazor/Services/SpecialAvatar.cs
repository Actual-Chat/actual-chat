using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public static class SpecialAvatar
{
    public static readonly AvatarFull None = new(null!, Symbol.Empty, 0);
    public static readonly AvatarFull Loading = new(null!, Symbol.Empty, -1);
}
