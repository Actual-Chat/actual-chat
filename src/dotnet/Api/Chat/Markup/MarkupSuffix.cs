using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// Marks where a message's trailing marks - sending status, translation - are rendered.
/// Carries nothing itself: <see cref="MarkupSuffixInjector"/> places it, and the view renders
/// whatever the render context supplies.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed class MarkupSuffix : Markup
{
    public static readonly MarkupSuffix Instance = new();

    public override string Format() => "";
}
