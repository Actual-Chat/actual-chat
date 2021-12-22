using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.SignalR.Client;

public abstract class HubClientBase
{
    private readonly Lazy<HubConnection> _hubConnectionLazy;
    private ILogger? _log;

    protected IServiceProvider Services { get; }
    protected Uri HubUrl { get; }
    protected HubConnection HubConnection => _hubConnectionLazy.Value;
    protected MomentClockSet Clocks { get; }
    protected ILogger Log => _log ??= Services.LogFor(GetType());

    protected HubClientBase(string hubUrl, IServiceProvider services)
    {
        Services = services;
        Clocks = Services.Clocks();
        HubUrl = Services.UriMapper().ToAbsolute(hubUrl);
        _hubConnectionLazy = new(CreateHubConnection);

        Task.Run(() => EnsureConnected(CancellationToken.None).AsTask());
    }

    protected HubConnection CreateHubConnection()
    {
        Log.LogDebug("CreateHubConnection: Time: {Time}", Clocks.SystemClock.UtcNow.TimeOfDay);
        try {
            var builder = new HubConnectionBuilder()
                .WithUrl(
                    HubUrl,
    options => {
                        options.SkipNegotiation = true;
                        options.Transports = HttpTransportType.WebSockets;
                    })
                .WithAutomaticReconnect(new[] {
                    TimeSpan.FromMilliseconds(10d),
                    TimeSpan.FromMilliseconds(20d),
                    TimeSpan.FromMilliseconds(40d),
                    TimeSpan.FromMilliseconds(100d),
                    TimeSpan.FromMilliseconds(500d),
                    TimeSpan.FromMilliseconds(1000d),
                    TimeSpan.FromMilliseconds(2000d),
                });
            if (!Debugging.SignalR.DisableMessagePackProtocol)
                builder = builder.AddMessagePackProtocol();
            return builder.Build();
        }
        finally {
            Log.LogDebug("CreateHubConnection: Exited; Time: {Time}", Clocks.SystemClock.UtcNow.TimeOfDay);
        }
    }

    protected async ValueTask EnsureConnected(CancellationToken cancellationToken)
    {
        if (HubConnection.State == HubConnectionState.Connected)
            return;

        var retryDelay = 0.5d;
        var attempt = 0;
        while (HubConnection.State != HubConnectionState.Connected || attempt < 10) {
            try {
                attempt++;
                if (HubConnection.State == HubConnectionState.Disconnected)
                    await HubConnection.StartAsync(cancellationToken).ConfigureAwait(false);
                else
                    await Clocks.CpuClock.Delay(TimeSpan.FromSeconds(0.5), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogError(e,
                    "EnsureConnected failed to reconnect SignalR Hub, will retry after {RetryDelay}s",
                    retryDelay);
                await Clocks.CpuClock.Delay(TimeSpan.FromSeconds(retryDelay), cancellationToken)
                    .ConfigureAwait(false);
                retryDelay = Math.Min(10d, retryDelay * (1 + Random.Shared.NextDouble())); // Exp. growth to 10s
            }
        }
    }
}
