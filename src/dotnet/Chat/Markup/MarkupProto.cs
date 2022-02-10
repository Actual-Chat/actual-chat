namespace ActualChat.Chat;

internal class MarkupProto
{
    public MarkupProto(Markup markup)
        => Markup = markup;

    public Markup Markup { get; }
    public List<MarkupPart> Parts { get; } = new ();
}
