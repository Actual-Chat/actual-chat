namespace ActualChat.UI.Blazor.Components;

public interface IContextMenuBackend
{
    Task Toggle(bool? mustOpen = null);
}
