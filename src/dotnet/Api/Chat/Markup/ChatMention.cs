namespace ActualChat.Chat;

public sealed class ChatMention(MentionId id, string name = "") : MentionMarkup(id, name)
{
    public ChatId ChatId => (ChatId)Id.Target;
    public Chat? Chat { get; init; }
}
