using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Defines text styling options.
/// </summary>
public enum TextStyle { None = 0, Italic = 1, Bold = 2, Spoiler = 3 }

/// <summary>
/// Represents styled text content (bold, italic, spoiler).
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class StylizedMarkup(Markup content, TextStyle style) : Markup
{
    // U+2588 FULL BLOCK — renders a redaction-bar mask in notifications and other
    // non-interactive text surfaces where a spoiler can't be revealed by tapping.
    public const char SpoilerMaskChar = '█';

    public Markup Content { get; init; } = content;
    public TextStyle Style { get; init; } = style;

    public string StyleToken => Style switch {
        TextStyle.None => "",
        TextStyle.Italic => "*",
        TextStyle.Bold => "**",
        TextStyle.Spoiler => "||",
        _ => throw StandardError.Internal($"Invalid {nameof(Style)} property value."),
    };

    public override string Format()
    {
        var markupText = Content.Format();
        return IsIncomplete
            ? $"{StyleToken}{markupText}"
            : $"{StyleToken}{markupText}{StyleToken}";
    }

    public override Markup Simplify()
    {
        var markup = Content.Simplify();
        return ReferenceEquals(markup, Content)
            ? this
            : new StylizedMarkup(markup, Style) { IsIncomplete = IsIncomplete };
    }

    public static string Mask(string text)
    {
        if (text.Length == 0)
            return text;

        return string.Create(text.Length, text, static (span, s) => {
            for (var i = 0; i < s.Length; i++)
                span[i] = char.IsWhiteSpace(s[i]) ? s[i] : SpoilerMaskChar;
        });
    }
}
