using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

public enum TextStyle { None = 0, Italic = 1, Bold = 2 }

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class StylizedMarkup(Markup content, TextStyle style) : Markup
{
    public Markup Content { get; init; } = content;
    public TextStyle Style { get; init; } = style;

    public string StyleToken => Style switch {
        TextStyle.None => "",
        TextStyle.Italic => "*",
        TextStyle.Bold => "**",
        _ => throw StandardError.Internal($"Invalid {nameof(Style)} property value."),
    };

    public override string Format()
    {
        var markupText = Content.Format();
        return $"{StyleToken}{markupText}{StyleToken}";
    }

    public override Markup Simplify()
    {
        var markup = Content.Simplify();
        return ReferenceEquals(markup, Content) ? this : new StylizedMarkup(markup, Style);
    }
}
