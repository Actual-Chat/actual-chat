namespace ActualChat.Chat;

public sealed class AuthorMention(MentionId id, string name = "") : MentionMarkup(id, name)
{
    public AuthorId AuthorId => (AuthorId)Id.Target;
    public Author? Author { get; init; }
}
