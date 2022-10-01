using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Stl.Fusion.Bridge;

namespace ActualChat.SignalR.Client;

public abstract class HubClientBase : IDisposable
{
    protected Connector<HubConnection> Connector { get; }
    protected IServiceProvider Services { get; }
    protected ILogger Log { get; }

    public string HubUrl { get; init; }
    public RetryDelaySeq ReconnectDelays { get; init; } = new(0.5, 10);

    protected HubClientBase(string hubUrl, IServiceProvider services)
    {
        Services = services;
        Log = Services.LogFor(GetType());
        HubUrl = hubUrl;

 #pragma warning disable MA0056
        Connector = new(Connect, services.StateFactory()) {
            ReconnectDelays = ReconnectDelays,
            Log = Log,
            LogTag = $"SignalR hub @ {HubUrl}",
            LogLevel = LogLevel.Debug,
        };
 #pragma warning restore MA0056
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
            Connector.Dispose();
    }

    protected Task<HubConnection> GetConnection(CancellationToken cancellationToken)
        => Connector.GetConnection(cancellationToken);

    protected virtual async Task<HubConnection> Connect(CancellationToken cancellationToken)
    {
        var hubUri = Services.UrlMapper().GetHubUrl(HubUrl);
        var builder = new HubConnectionBuilder()
            .WithUrl(hubUri, options => {
                options.SkipNegotiation = true;
                options.Transports = HttpTransportType.WebSockets;
            });
        if (Constants.DebugMode.SignalR)
            builder.AddJsonProtocol();
        else
            builder.AddMessagePackProtocol();

        var connection = builder.Build();
        try {
            await connection.StartAsync(cancellationToken).ConfigureAwait(false);
            connection.Closed += e => {
                Connector.DropConnection(connection, e);
                return Task.CompletedTask;
            };
            return connection;
        }
        catch (Exception) {
            await connection.DisposeSilentlyAsync().ConfigureAwait(false);
            throw;
        }
    }
}
