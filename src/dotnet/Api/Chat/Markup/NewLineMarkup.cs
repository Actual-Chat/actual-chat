using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed class NewLineMarkup() : TextMarkup("\r\n")
{
    public static readonly NewLineMarkup Instance = new();

    public override TextMarkupKind Kind => TextMarkupKind.NewLine;

    public override TextMarkup WithText(string text) => this;
}
