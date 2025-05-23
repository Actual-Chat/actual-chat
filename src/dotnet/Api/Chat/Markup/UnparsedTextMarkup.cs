using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class UnparsedTextMarkup(string text) : TextMarkup(text)
{
    public static new readonly UnparsedTextMarkup Empty = new("");

    public override TextMarkupKind Kind => TextMarkupKind.Unparsed;

    public override TextMarkup WithText(string text)
        => new UnparsedTextMarkup(text);
}
