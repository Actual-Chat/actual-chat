using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class PreformattedTextMarkup(string text) : TextMarkup(text)
{
    public static new readonly PreformattedTextMarkup Empty = new("");

    public override TextMarkupKind Kind => TextMarkupKind.Preformatted;

    public PreformattedTextMarkup() : this("") { }

    public override string Format()
        => $"`{Text.OrdinalReplace("`", "``")}`";

    public override TextMarkup WithText(string text)
        => new PreformattedTextMarkup(text);
}
