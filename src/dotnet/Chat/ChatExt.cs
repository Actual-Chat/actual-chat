namespace ActualChat.Chat;

public static class ChatExt
{
    public static bool CanUseAnonymousAuthor(this Chat chat)
        => chat.AllowedAuthorKind == ChatAuthorKind.Any;
    public static bool CanUseRegularAuthor(this Chat chat) =>
        chat.AllowedAuthorKind == ChatAuthorKind.Any ||
        chat.AllowedAuthorKind == ChatAuthorKind.RegularOnly;
}
