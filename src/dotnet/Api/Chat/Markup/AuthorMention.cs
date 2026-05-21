namespace ActualChat.Chat;

public sealed class AuthorMention(MentionId id, string name = "") : MentionMarkup(id, name)
{
    public AuthorId AuthorId => (AuthorId)Id.TargetId;
    public Author? Author { get; init; }
}
