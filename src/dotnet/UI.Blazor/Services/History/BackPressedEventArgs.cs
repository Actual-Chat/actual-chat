namespace ActualChat.UI.Blazor.Services;

public class BackPressedEventArgs : EventArgs
{
    public BackPressedEventArgs(Action moveToBack)
        => MoveToBack = moveToBack;

    public bool Handled { get; set; }
    public Action MoveToBack { get; }
}
