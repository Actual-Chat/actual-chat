namespace ActualChat.UI.Blazor.Components;

public sealed record ShareModalModel(
    ShareKind Kind,
    string Title,
    string TargetTitle,
    ShareRequest Request,
    IShareModalSelector? SelectorPrefs,
    string? ImageUrl = null);

public interface IShareModalSelector;

public record ShareWithPlaceMembersOnly(PlaceId PlaceId) : IShareModalSelector
{
    public static ShareWithPlaceMembersOnly? GetFor(Chat.Chat chat, Chat.Place? place)
    {
        if (chat.Id is not PlaceChatId)
            return null;
        if (chat.IsPublic || place?.IsPublic != false)
            return null;

        return new ShareWithPlaceMembersOnly(place.Id);
    }
}
