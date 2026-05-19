namespace ActualChat.Chat;

public sealed class EmojiMention(MentionId id, string name = "") : MentionMarkup(id, name)
{
    public EmojiRef EmojiRef => (EmojiRef)Id.Target;
    public string? Glyph { get; init; }
    public Picture? CustomPicture { get; init; }
}
