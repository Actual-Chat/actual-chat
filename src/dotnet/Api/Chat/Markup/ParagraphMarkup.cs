namespace ActualChat.Chat;

public class ParagraphMarkup : Markup
{
    public static new readonly ParagraphMarkup Empty = new (PlainTextMarkup.Empty);

    public ParagraphMarkup(Markup content)
    {
        if (content.IsBlockMarkup())
            throw new ArgumentException("Content must not be a block markup", nameof(content));
        Content = content;
    }

    public Markup Content { get; }

    public override string Format()
        => Content.Format();

    public override Markup Simplify()
    {
        var simplified = Content.Simplify();
        return simplified == Content ? this : new ParagraphMarkup(simplified);
    }
}
