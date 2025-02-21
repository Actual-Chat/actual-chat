namespace ActualChat.UI.Blazor;

public interface IUIActionHandler
{
    public bool IsBusy { get; }
    public bool IsReady { get; }
    public Task Execute();
}

public interface IUIActionHandler<in TParameter>
{
    public bool IsBusy { get; }
    public bool IsReady { get; }
    public Task Execute(TParameter parameter);
}

public static class UIActionHandler
{
    public static IUIActionHandler<TArg> Create<TArg>(ComponentBase component, Func<TArg, Task> taskFactory)
        => new UIActionHandler<TArg>(taskFactory, component.NotifyStateHasChanged);

    public static IUIActionHandler Create(ComponentBase component, Func<Task> taskFactory)
        => new UIActionHandlerVoid(taskFactory, component.NotifyStateHasChanged);
}

public class UIActionHandlerVoid(Func<Task> taskFactory, Action stateHasChanged) : IUIActionHandler
{
    private readonly UIActionHandler<string> _innerHandler = new (_ => taskFactory.Invoke(), stateHasChanged);

    public bool IsBusy => _innerHandler.IsBusy;
    public bool IsReady => _innerHandler.IsReady;
    public Task Execute()
        => _innerHandler.Execute(string.Empty);
}

public class UIActionHandler<TArg>(Func<TArg, Task> taskFactory, Action stateHasChanged) : IUIActionHandler<TArg>
{
    public bool IsBusy { get; private set; }
    public bool IsReady => !IsBusy;

    public async Task Execute(TArg arg)
    {
        try {
            if (IsBusy)
                return;

            IsBusy = true;
            var task = taskFactory.Invoke(arg);
            if (!task.IsCompleted)
                stateHasChanged();
            await task.ConfigureAwait(true); // Continue on the UI thread.
        }
        finally {
            IsBusy = false;
            stateHasChanged();
        }
    }
}
