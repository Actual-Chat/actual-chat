namespace ActualChat.UI.App.Services;

public abstract class AppServerInstanceSelector
{
    public abstract AppServerInstance Default { get; }
    public abstract AppServerInstance Current { get; }
    public abstract AppServerInstance Get();
    public abstract void Set(AppServerInstance instance);
}
