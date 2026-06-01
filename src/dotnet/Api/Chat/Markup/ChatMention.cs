namespace ActualChat.Chat;

public sealed class ChatMention(MentionRef id, string name = "") : MentionMarkup(id, name)
{
    public ChatId ChatId => (ChatId)Id.Target;
    public Chat? Chat { get; init; }
}
