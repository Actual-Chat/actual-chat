using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Represents a Markdown-style horizontal divider (a line of three or more dashes).
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed class DividerMarkup : BlockMarkup
{
    public const string Text = "---";

    public override string Format()
        => Text;
}
