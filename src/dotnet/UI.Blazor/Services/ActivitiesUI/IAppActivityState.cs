namespace ActualChat.UI.Blazor.Services;

public interface IAppActivityState
{
    IState<AppActivityState> State { get; }
    IState<bool> IsBackground { get; }
}
