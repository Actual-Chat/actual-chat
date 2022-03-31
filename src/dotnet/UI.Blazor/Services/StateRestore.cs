namespace ActualChat.UI.Blazor.Services;

public sealed class StateRestore
{
    private ILogger<StateRestore> Log { get; }
    private IServiceProvider Services { get; }
    private IEnumerable<IStateRestoreHandler> Handlers
        => Services.GetRequiredService<IEnumerable<IStateRestoreHandler>>();

    public StateRestore(
        IServiceProvider services,
        ILogger<StateRestore> log)
    {
        Log = log;
        Services = services;
    }

    public async Task Restore(CancellationToken cancellationToken)
    {
        foreach (var handler in Handlers.OrderBy(c => c.Priority))
            await handler.Restore(cancellationToken).ConfigureAwait(true);
    }
}
