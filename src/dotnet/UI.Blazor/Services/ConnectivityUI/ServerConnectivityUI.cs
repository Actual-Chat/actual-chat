namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Server-side implementation: always online, always connected.
/// </summary>
public sealed class ServerConnectivityUI : ConnectivityUI
{
    public override IState<bool> IsOnline { get; }

    public ServerConnectivityUI(UIHub hub) : base(hub)
    {
        IsOnline = hub.StateFactory.NewMutable(true, StateCategories.Get(GetType(), nameof(IsOnline)));
        _ = Initialize();
    }
}
