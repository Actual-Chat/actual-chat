namespace ActualChat.UI.Blazor.Components;

public static class MediaUploadScopes
{
    public static string ForUser(UserId userId) => userId.Value;
    public static string ForAccount(AccountFull account) => ForUser(account.Id);
    public static string ForChat(ChatId chatId) => chatId.Value;
    public static string ForPlace(PlaceId placeId) => ForChat(placeId.RootChatId);
}
