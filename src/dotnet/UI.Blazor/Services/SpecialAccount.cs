using ActualChat.Users;

namespace ActualChat.UI.Blazor.Services;

public static class SpecialAccount
{
    public static readonly AccountFull None = new(User.NewGuest(), 0) {
        Avatar = SpecialAvatar.None,
    };
    public static readonly AccountFull Loading = new(User.NewGuest(), -1) {
        Avatar = SpecialAvatar.Loading,
    };
}
