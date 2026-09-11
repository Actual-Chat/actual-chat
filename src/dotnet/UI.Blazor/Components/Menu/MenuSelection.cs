namespace ActualChat.UI.Blazor.Components;

/// <summary>
/// The text selected when a menu opened, clamped to the element whose
/// <c>data-menu</c> is <see cref="OwnerMenuRef"/>.
/// </summary>
public sealed record MenuSelection(string Text, MenuRef OwnerMenuRef);
