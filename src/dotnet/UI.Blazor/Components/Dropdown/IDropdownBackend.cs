namespace ActualChat.UI.Blazor.Components;

public interface IDropdownBackend
{
    Task Toggle(bool? mustOpen = null);
}
