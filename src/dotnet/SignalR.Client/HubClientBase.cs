using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.SignalR.Client;

public abstract class HubClientBase
{
    private readonly Lazy<HubConnection> _hubConnectionLazy;

    protected IServiceProvider Services { get; }
    protected Uri HubUrl { get; }
    protected HubConnection HubConnection => _hubConnectionLazy.Value;
    protected MomentClockSet Clocks { get; }
    protected ILogger Log { get; }

    protected HubClientBase(IServiceProvider services, string hubUrl)
    {
        Services = services;
        Log = Services.LogFor(GetType());
        Clocks = Services.Clocks();
        HubUrl = Services.UriMapper().ToAbsolute(hubUrl);
        _hubConnectionLazy = new(CreateHubConnection);
    }

    protected HubConnection CreateHubConnection()
    {
        var builder = new HubConnectionBuilder()
            .WithUrl(HubUrl)
            .WithAutomaticReconnect();
        if (!Debugging.SignalR.DisableMessagePackProtocol)
            builder = builder.AddMessagePackProtocol();
        return builder.Build();
    }

    protected async ValueTask EnsureConnected(CancellationToken cancellationToken)
    {
        if (HubConnection.State == HubConnectionState.Connected)
            return;

        var retryDelay = 0.5d;
        var attempt = 0;
        while (HubConnection.State != HubConnectionState.Connected || attempt < 10)
            try {
                attempt++;
                if (HubConnection.State == HubConnectionState.Disconnected)
                    await HubConnection.StartAsync(cancellationToken).ConfigureAwait(false);
                else
                    await Clocks.CpuClock.Delay(TimeSpan.FromSeconds(0.5), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogError(e,
                    "EnsureConnected failed to reconnect SignalR Hub, will retry after {RetryDelay}s", retryDelay);
                await Clocks.CpuClock.Delay(TimeSpan.FromSeconds(retryDelay), cancellationToken)
                    .ConfigureAwait(false);
                retryDelay = Math.Min(10d, retryDelay * (1 + Random.Shared.NextDouble())); // Exp. growth to 10s
            }
    }
}
