using ActualChat.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.SignalR.Client;

public abstract class HubClientBase : WorkerBase
{
    private Task<HubConnection>? _connectTask;

    private Task<HubConnection>? ConnectTask {
        get => Interlocked.CompareExchange(ref _connectTask, null, null);
        set => Interlocked.Exchange(ref _connectTask, value);
    }

    protected IServiceProvider Services { get; }
    protected MomentClockSet Clocks { get; }
    protected ILogger Log { get; }

    protected Uri HubUrl { get; }
    protected Func<IHubConnectionBuilder> ConnectionBuilderFactory { get; init; }
    protected TimeSpan GetConnectionRetryDelay { get; init; } = TimeSpan.FromSeconds(0.1);
    protected RetryDelaySeq RetryDelays { get; init; } = new(0.5, 10, 0.25);

    protected HubClientBase(string hubRelativeUrl, IServiceProvider services)
        : this(services.UriMapper().ToAbsolute(hubRelativeUrl), services)
    { }

    protected HubClientBase(Uri hubUrl, IServiceProvider services)
    {
        Services = services;
        Clocks = Services.Clocks();
        Log = Services.LogFor(GetType());

        // Workaround for missing SSL CA cert for local.actual.chat
        var hostInfo = Services.GetRequiredService<HostInfo>();
        if (hostInfo.IsDevelopmentInstance
            && hubUrl.ToString().OrdinalHasPrefix("https://local.actual.chat/backend/hub/", out var suffix))
            hubUrl = new Uri("http://local.actual.chat:7080/backend/hub/" + suffix);

        HubUrl = hubUrl;
        ConnectionBuilderFactory = () => {
            var builder = new HubConnectionBuilder()
                .WithUrl(HubUrl, options => {
                    options.SkipNegotiation = true;
                    options.Transports = HttpTransportType.WebSockets;
                });
            if (Constants.DebugMode.SignalR)
                builder.AddJsonProtocol();
            else
                builder.AddMessagePackProtocol();
            return builder;
        };
    }

    protected async ValueTask<HubConnection> GetHubConnection(CancellationToken cancellationToken)
    {
        Start();
        while (true) {
            var connectTask = ConnectTask;
            if (connectTask != null) {
                var connectResult = await connectTask.WaitResultAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (connectResult.IsValue(out var hubConnection) && hubConnection.State == HubConnectionState.Connected)
                    return hubConnection;
            }
            // Technically there could be no delay at all, but let's have a short one
            await Task.Delay(GetConnectionRetryDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var retryIndex = 0;
        while (true) {
            try {
                ConnectTask = Connect(cancellationToken);
                var hubConnection = await ConnectTask.ConfigureAwait(false);
                await using var connection = hubConnection.ConfigureAwait(false);
                retryIndex = 0; // Reset on connect

                var closedTaskSource = TaskSource.New<string?>(false);
                var onClosed = (Func<Exception?, Task>) null!;
                onClosed = error => {
                    error ??= new ChannelClosedException("SignalR connection is closed.");
                    hubConnection.Closed -= onClosed;
                    closedTaskSource.TrySetException(error);
                    return Task.CompletedTask;
                };
                hubConnection.Closed += onClosed;
                try {
                    await closedTaskSource.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                finally {
                    hubConnection.Closed -= onClosed;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                var retryDelay = RetryDelays[++retryIndex];
                Log.LogError(e,
                    "Failed to connect to SignalR hub @ {HugUrl}, will retry in {RetryDelay} (#{RetryIndex})",
                    HubUrl, retryDelay.ToShortString(), retryIndex);
                await Clocks.CpuClock.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<HubConnection> Connect(CancellationToken cancellationToken)
    {
        Log.LogDebug("Connecting to SignalR hub @ {HubUrl}", HubUrl);
        var hubConnection = ConnectionBuilderFactory.Invoke().Build();
        try {
            await hubConnection.StartAsync(cancellationToken).ConfigureAwait(false);
            Log.LogDebug("Connected to SignalR hub @ {HubUrl}", HubUrl);
            return hubConnection;
        }
        catch (Exception) {
            await hubConnection.DisposeSilentlyAsync().ConfigureAwait(false);
            throw;
        }
    }
}
