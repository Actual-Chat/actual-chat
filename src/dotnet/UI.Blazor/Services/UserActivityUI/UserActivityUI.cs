using ActualChat.UI.Blazor.Module;

namespace ActualChat.UI.Blazor.Services;

public class UserActivityUI : IUserActivityUIBackend, IDisposable
{
    private readonly Task _whenInitialized;
    private readonly IComputedState<Moment> _lastActiveAt;
    private IJSObjectReference _jsRef = null!;
    private DotNetObjectReference<IUserActivityUIBackend> _blazorRef = null!;

    public IState<Moment> LastActiveAt => _lastActiveAt;
    private IJSRuntime JS { get; }

    public UserActivityUI(IServiceProvider services)
    {
        JS = services.GetRequiredService<IJSRuntime>();
        var stateFactory = services.GetRequiredService<IStateFactory>();
        _whenInitialized = Initialize();
        _lastActiveAt = stateFactory.NewComputed<Moment>(
            new () { ComputedOptions = new () { AutoInvalidationDelay = Constants.Presence.UpdatePeriod } },
            (_, token) => GetLastActiveAt(token));
    }

    public ValueTask SubscribeForNext(CancellationToken cancellationToken)
        => _jsRef.InvokeVoidAsync("subscribeForNext", cancellationToken);

    [JSInvokable]
    public Task OnInteracted()
    {
        _lastActiveAt.Recompute();
        return Task.CompletedTask;
    }

    private async Task Initialize()
    {
        _blazorRef = DotNetObjectReference.Create<IUserActivityUIBackend>(this);
        _jsRef = await JS.InvokeAsync<IJSObjectReference>($"{BlazorUICoreModule.ImportName}.UserActivityUI.create", _blazorRef);
    }

    private async Task<Moment> GetLastActiveAt(CancellationToken cancellationToken)
    {
        if (!_whenInitialized.IsCompletedSuccessfully)
            await _whenInitialized.ConfigureAwait(false);
        return await _jsRef.InvokeAsync<Moment>("getLastActiveAt", cancellationToken);
    }

    public void Dispose()
    {
        _lastActiveAt.Dispose();
        _blazorRef.Dispose();
    }
}

public interface IUserActivityUIBackend
{
    Task OnInteracted();
}
