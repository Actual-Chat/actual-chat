using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class ListItemMarkup(Markup content) : Markup
{
    public Markup Content { get; } = content;

    public override string Format()
        => GetPrefix() + Content.Format();

    public string GetPrefix()
        => "- ";

    public override Markup Simplify()
    {
        var content2 = Content.Simplify();
        if (ReferenceEquals(content2, Content))
            return this;

        return new ListItemMarkup(content2);
    }
}
