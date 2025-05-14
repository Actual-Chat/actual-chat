namespace ActualChat.Chat;

public static class ChatExt
{
    public static bool IsMember(this Chat chat)
        => chat.Rules.IsMember();

    public static bool IsMember(this AuthorRules authorRules)
        => authorRules.Author is { HasLeft: false };

    public static bool IsAiSearchChat([NotNullWhen(true)] this Chat? chat)
        => chat is not null && chat.SystemTag == Constants.Chat.SystemTags.Bot;

    public static bool IsChatRoulette([NotNullWhen(true)] this Chat? chat)
        => chat is not null && chat.SystemTag == Constants.Chat.SystemTags.ChatRoulette;
}
