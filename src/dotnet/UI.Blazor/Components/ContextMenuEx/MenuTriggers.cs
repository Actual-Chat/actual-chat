namespace ActualChat.UI.Blazor.Components;

[Flags]
public enum MenuTriggers
{
    None = 0,
    LeftClick = 1,
    RightClick = 2,
    LongClick = 4,
}
