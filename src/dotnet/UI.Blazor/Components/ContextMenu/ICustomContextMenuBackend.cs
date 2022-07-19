namespace ActualChat.UI.Blazor.Components;

public interface ICustomContextMenuBackend
{
    Task Toggle(bool? mustOpen = null);
}
