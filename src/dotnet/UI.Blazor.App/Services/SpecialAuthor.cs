using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public static class SpecialAuthor
{
    public static readonly AuthorFull None = new(null!, null!, 0) {
        Avatar = SpecialAvatar.None,
    };
    public static readonly AuthorFull Loading = new(null!, null!, -1) {
        Avatar = SpecialAvatar.Loading,
    };
}
