using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Represents text that should not be parsed for markup.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed class UnparsedTextMarkup(string text) : TextMarkup(text)
{
    public static readonly UnparsedTextMarkup Empty = new("");

    public override TextMarkupKind Kind => TextMarkupKind.Unparsed;

    public override TextMarkup WithText(string text)
        => new UnparsedTextMarkup(text);
}
