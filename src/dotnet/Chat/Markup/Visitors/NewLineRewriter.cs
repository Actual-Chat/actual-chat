namespace ActualChat.Chat;

public class NewLineRewriter : MarkupRewriter
{
    public static readonly NewLineRewriter Instance = new NewLineRewriter();

    protected override Markup VisitPlainText(PlainTextMarkup markup)
        => OrdinalEquals(markup.Text, "\n") || OrdinalEquals(markup.Text, "\r\n")
            ? Markup.NewLine
            : markup;
}
