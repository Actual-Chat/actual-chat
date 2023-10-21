namespace ActualChat.UI.Blazor;

public sealed class NoStateHasChanged : IHandleEvent
{
    public static readonly NoStateHasChanged Instance = new ();

    private NoStateHasChanged() { }

    public static EventCallback EventCallback(Action eventHandler)
        => new(Instance, eventHandler);
    public static EventCallback<T> EventCallback<T>(Action<T> eventHandler)
        => new(Instance, eventHandler);
    public static EventCallback EventCallback(Func<Task> eventHandler)
        => new(Instance, eventHandler);
    public static EventCallback<T> EventCallback<T>(Func<T, Task> eventHandler)
        => new(Instance, eventHandler);

    // Private methods

    Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem item, object? arg)
        => item.InvokeAsync(arg);
}
