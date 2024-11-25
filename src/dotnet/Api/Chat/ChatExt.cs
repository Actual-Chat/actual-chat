namespace ActualChat.Chat;

public static class ChatExt
{
    public static bool IsMember(this Chat chat)
        => chat.Rules.IsMember();

    public static bool IsMember(this AuthorRules authorRules)
        => authorRules.Author is { HasLeft: false };

    public static bool IsAiSearchChat(this Chat chat)
        => chat.SystemTag == Constants.Chat.SystemTags.Bot;
}
