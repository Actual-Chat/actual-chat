using ActualLab.Interception;

namespace ActualChat.UI.Blazor.Pages.ComputeStateTestPage;

public class ComputeStateTestService : SafeAsyncDisposableBase, IHasServices, IComputeService, INotifyInitialized
{
    private readonly IMutableState<string> _state;

    public IServiceProvider Services { get; }
    public IState<string> State => _state;

    public ComputeStateTestService(IServiceProvider services)
    {
        Services = services;
        var stateFactory = services.StateFactory();
        _state = stateFactory.NewMutable<string>("init");
    }

    public void Initialized()
    { }

    protected override Task DisposeAsync(bool disposing)
        => Task.CompletedTask;

    [ComputeMethod]
    public virtual Task<string> GetValue(CancellationToken cancellationToken)
        // var latestValue = await _state.Use(cancellationToken).ConfigureAwait(false);
        // return latestValue;
        => Task.FromResult(_state.Value);

    public virtual async Task MutateAndInvalidate(string finalValue, CancellationToken cancellationToken)
    {
        _state.Value = "start";
        using (ComputeContext.BeginInvalidation())
            _ = GetValue(default);

        for (int i = 0; i < 100; i++) {
            if (i % 10 == 0)
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);

            _state.Value = i.ToString(CultureInfo.InvariantCulture);
        }
        _state.Value = finalValue;
        using (ComputeContext.BeginInvalidation())
            _ = GetValue(default);
    }
}
