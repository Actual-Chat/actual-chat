using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public static class NavbarExt
{
    private const string PlacePrefix = "place-";
    private const string PinnedChatPrefix = "pinned-chat-";

    public static string GetNavbarGroupId(this PlaceId placeId)
        => PlacePrefix + placeId.Value;

    public static bool IsPlaceSelected(this NavbarUI navbarUI, out PlaceId placeId)
    {
        placeId = PlaceId.None;
        var groupId = navbarUI.SelectedGroupId;
        if (!groupId.OrdinalStartsWith(PlacePrefix))
            return false;

        var sPlaceId = groupId.Substring(PlacePrefix.Length);
        placeId = new PlaceId(sPlaceId, AssumeValid.Option);
        return true;
    }

    public static string GetNavbarGroupId(this ChatId pinnedChatId)
        => PinnedChatPrefix + pinnedChatId.Value;

    public static bool IsPinnedChatSelected(this NavbarUI navbarUI, out ChatId chatId)
    {
        chatId = ChatId.None;
        var groupId = navbarUI.SelectedGroupId;
        if (!groupId.OrdinalStartsWith(PinnedChatPrefix))
            return false;

        var sChatId = groupId.Substring(PinnedChatPrefix.Length);
        chatId = ChatId.ParseOrNone(sChatId);
        return !chatId.IsNone;
    }
}
