namespace ActualChat.Chat;

public class NewLineRewriter : MarkupRewriter
{
    public static readonly NewLineRewriter Instance = new NewLineRewriter();

    protected override Markup VisitPlainText(PlainTextMarkup markup)
    {
        if (string.Equals(markup.Text, "\n", StringComparison.Ordinal)
            || string.Equals(markup.Text, "\r\n", StringComparison.Ordinal))
            return Markup.NewLine;
        return markup;
    }
}
