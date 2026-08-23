namespace ActualChat.UI.Blazor.Components;

/// <summary>
/// What <see cref="TabPanel"/> plays when the user picks another tab. The direction comes from
/// where that tab sits in the rail, so a member here is a family rather than one effect.
/// </summary>
public enum TabContentSwap
{
    None = 0, // The tab body cuts, which is what every panel did before
    Swipe, // Both layers slide, the incoming tab pushing the outgoing one out
    Wipe, // A dissolve front sweeps across the outgoing tab, the incoming one standing still
}
