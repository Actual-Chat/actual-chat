using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace ActualChat.SignalR.Client;

public abstract class HubClientBase
{
    private readonly Lazy<HubConnection> _hubConnectionLazy;

    protected IServiceProvider Services { get; }
    protected Uri HubUrl { get; }
    protected HubConnection HubConnection => _hubConnectionLazy.Value;
    protected ILogger Log { get; }

    protected HubClientBase(IServiceProvider services, string hubUrl)
    {
        Services = services;
        Log = Services.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());
        HubUrl = Services.UriMapper().ToAbsolute(hubUrl);
        _hubConnectionLazy = new (CreateHubConnection);
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

        var delayInterval = 500;
        var attempt = 0;
        while (HubConnection.State != HubConnectionState.Connected || attempt < 10)
            try {
                attempt++;
                if (HubConnection.State == HubConnectionState.Disconnected)
                    await HubConnection.StartAsync(cancellationToken).ConfigureAwait(false);
                else
                    await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException e) {
                Log.LogError(e, "HubClientBase.EnsureConnected - Operation cancelled");
                throw;
            }
            catch (Exception e) {
                Log.LogError(e, "HubClientBase.EnsureConnected - Failed to reconnect SignalR Hub");
                await Task.Delay(delayInterval, cancellationToken).ConfigureAwait(false);
                if (delayInterval < 10000)
                    delayInterval += Random.Shared.Next(1000);
            }
    }
}
