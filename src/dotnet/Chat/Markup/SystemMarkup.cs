namespace ActualChat.Chat;

public sealed record SystemMarkup(Markup Content) : Markup
{
    public SystemMarkup() : this(null!) { }

    public override string Format()
        => Content.Format();

    public override Markup Simplify()
    {
        var markup = Content.Simplify();
        return ReferenceEquals(markup, Content) ? this : new SystemMarkup(markup);
    }
}
