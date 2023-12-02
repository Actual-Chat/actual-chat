using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Stl.Net;

namespace ActualChat.SignalR;

public abstract class HubClientBase : IDisposable
{
    protected Connector<HubConnection> Connector { get; }
    protected IServiceProvider Services { get; }
    protected ILogger Log { get; }

    public string HubUrl { get; init; }

    protected HubClientBase(string hubUrl, IRetryDelayer reconnectDelayer, IServiceProvider services)
    {
        Services = services;
        Log = services.LogFor(GetType());
        HubUrl = hubUrl;
        Connector = new(Connect) {
            ReconnectDelayer = reconnectDelayer,
            TerminalErrorDetector = e => e is ObjectDisposedException,
            Log = Log,
            LogTag = $"SignalR hub @ {HubUrl}",
            LogLevel = LogLevel.Debug,
        };
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
        connection.Closed += e => {
            Connector.DropConnection(connection, e);
            return Task.CompletedTask;
        };
        try {
            await connection.StartAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch (Exception) {
            await connection.DisposeSilentlyAsync().ConfigureAwait(false);
            throw;
        }
    }
}
