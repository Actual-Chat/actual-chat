namespace ActualChat.UI.Blazor.Components;

public sealed record ShareModalModel(
    ShareKind Kind,
    string Title,
    string TargetTitle,
    ShareRequest Request,
    IShareModalSelector? SelectorPrefs);

public interface IShareModalSelector;

public record ShareWithPlaceMembersOnly(PlaceId PlaceId) : IShareModalSelector
{
    public static ShareWithPlaceMembersOnly? GetFor(Chat.Chat chat, Chat.Place? place)
    {
        if (!chat.Id.IsPlaceChat)
            return null;

        return !chat.IsPublic && place?.IsPublic == false ? new ShareWithPlaceMembersOnly(place.Id) : null;
    }
}
