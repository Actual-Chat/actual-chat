using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Represents a line break in markup.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed class NewLineMarkup() : TextMarkup("\r\n")
{
    public static readonly NewLineMarkup Instance = new();

    public override TextMarkupKind Kind => TextMarkupKind.NewLine;

    public override TextMarkup WithText(string text) => this;
}
