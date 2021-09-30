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
        _hubConnectionLazy = new Lazy<HubConnection>(CreateHubConnection);
    }

    protected virtual HubConnection CreateHubConnection()
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
        if  (HubConnection.State != HubConnectionState.Disconnected)
            return;

        var random = new Random();
        var delayInterval = 500;
        var attempt = 0;
        while (attempt < 10)
            try {
                attempt++;
                if (HubConnection.State == HubConnectionState.Disconnected)
                    await HubConnection.StartAsync(cancellationToken);
                else
                    return;
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch(Exception e) {
                Log.LogError(e, "Failed to reconnect SignalR Hub");
                await Task.Delay(delayInterval, cancellationToken);
                if (delayInterval < 5000)
                    delayInterval += random.Next(1000);
            }
    }
}
