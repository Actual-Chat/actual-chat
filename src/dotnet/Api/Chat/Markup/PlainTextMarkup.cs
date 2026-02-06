using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Represents plain text content without formatting.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class PlainTextMarkup(string text) : TextMarkup(text)
{
    public static new readonly PlainTextMarkup Empty = new("");

    public override TextMarkupKind Kind => TextMarkupKind.Plain;

    public override TextMarkup WithText(string text)
        => new PlainTextMarkup(text);
}
