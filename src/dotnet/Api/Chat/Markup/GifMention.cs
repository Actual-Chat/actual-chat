namespace ActualChat.Chat;

public sealed class GifMention(MentionId id, string name = "") : MentionMarkup(id, name)
{
    public GifRef GifRef => (GifRef)Id.TargetId;
    public Picture? Picture { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
}
