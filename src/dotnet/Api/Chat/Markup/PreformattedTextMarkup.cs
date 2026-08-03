using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Represents inline monospace/code text wrapped in backticks.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class PreformattedTextMarkup(string text) : TextMarkup(text)
{
    public static readonly PreformattedTextMarkup Empty = new("");

    public override TextMarkupKind Kind => TextMarkupKind.Preformatted;

    public PreformattedTextMarkup() : this("") { }

    public override string Format()
    {
        var escapedText = Text.Replace("`", "``");
        return IsIncomplete ? $"`{escapedText}" : $"`{escapedText}`";
    }

    public override TextMarkup WithText(string text)
        => new PreformattedTextMarkup(text) { IsIncomplete = IsIncomplete };
}
