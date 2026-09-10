namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// What a message's markup views need that isn't in the markup: currently the trailing marks
/// (sending status, translation), rendered wherever <see cref="Chat.MarkupSuffix"/> was placed.
/// </summary>
/// <remarks>
/// Cascaded once per message. The alternative - handing the fragment to the last view - meant every
/// container suppressing it for all but its last child, which cost a CascadingValue per child.
/// </remarks>
public sealed record MarkupRenderContext(RenderFragment? Suffix);
