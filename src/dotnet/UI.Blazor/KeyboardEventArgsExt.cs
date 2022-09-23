namespace ActualChat.UI.Blazor;

public static class KeyboardEventArgsExt
{
    public static bool HasNoModifier(this KeyboardEventArgs e)
        => !e.AltKey && !e.CtrlKey && !e.ShiftKey && !e.MetaKey;
}
