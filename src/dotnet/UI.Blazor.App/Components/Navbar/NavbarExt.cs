using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public static class NavbarExt
{
    private const string PlacePrefix = "place-";
    private const string PinnedChatPrefix = "pinned-chat-";

    public static string GetNavbarGroupId(this PlaceId placeId)
        => PlacePrefix + placeId.Value;

    public static string GetNavbarGroupId(this ChatId pinnedChatId)
        => PinnedChatPrefix + pinnedChatId.Value;

    public static bool IsGroupSelected(this NavbarUI navbarUI, string groupId)
        => navbarUI.SelectedGroupId.Equals(groupId, StringComparison.Ordinal);

    public static bool IsPlaceSelected(this NavbarUI navbarUI, [NotNullWhen(true)] out PlaceId? placeId)
    {
        placeId = null;
        var groupId = navbarUI.SelectedGroupId;
        if (!groupId.OrdinalStartsWith(PlacePrefix))
            return false;

        var sPlaceId = groupId.Substring(PlacePrefix.Length);
        placeId = PlaceId.TryParse(sPlaceId);
        return placeId is not null;
    }

    public static bool IsPinnedChatSelected(this NavbarUI navbarUI, [NotNullWhen(true)] out ChatId? chatId)
    {
        chatId = null;
        var groupId = navbarUI.SelectedGroupId;
        if (!groupId.OrdinalStartsWith(PinnedChatPrefix))
            return false;

        var sChatId = groupId.Substring(PinnedChatPrefix.Length);
        chatId = ChatId.TryParse(sChatId);
        return chatId is not null;
    }
}
