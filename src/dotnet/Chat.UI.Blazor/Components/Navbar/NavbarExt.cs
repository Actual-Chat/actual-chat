using ActualChat.Chat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public static class NavbarExt
{
    public static string GetNavbarGroupId(this PlaceId placeId)
        => NavbarUI.PlacePrefix + placeId.Value;

    public static string GetNavbarGroupId(this ChatId pinnedChatId)
        => NavbarUI.PinnedChatPrefix + pinnedChatId.Value;
}
