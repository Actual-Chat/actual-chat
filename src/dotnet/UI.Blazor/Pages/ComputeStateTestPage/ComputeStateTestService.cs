﻿using Stl.Interception;

namespace ActualChat.UI.Blazor.Pages.ComputeStateTestPage;

public class ComputeStateTestService : IHasServices, IComputeService, INotifyInitialized
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

    public Task<string> GetValue(CancellationToken cancellationToken)
        // var latestValue = await _state.Use(cancellationToken).ConfigureAwait(false);
        // return latestValue;
        => Task.FromResult(_state.Value);

    public async Task MutateAndInvalidate(string finalValue, CancellationToken cancellationToken)
    {
        for (int i = 0; i < 100; i++) {
            if (i % 10 == 0)
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);

            _state.Value = i.ToString(CultureInfo.InvariantCulture);
        }
        _state.Value = finalValue;
        using (Computed.Invalidate())
            _ = GetValue(default);
    }
}
