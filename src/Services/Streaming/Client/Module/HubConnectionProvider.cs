using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace ActualChat.Streaming.Client.Module
{
    public class HubConnectionProvider : IHubConnectionProvider
    {
        private readonly HubConnection _hubConnection;
        private readonly ILogger<HubConnectionProvider> _logger;

        public HubConnectionProvider(HubConnection hubConnection, ILogger<HubConnectionProvider> logger)
        {
            _hubConnection = hubConnection;
            _logger = logger;
        }

        public async Task<HubConnection> GetConnection(CancellationToken cancellationToken)
        {
            await EnsureConnected(cancellationToken);
            return _hubConnection;
        }

        private async ValueTask EnsureConnected(CancellationToken cancellationToken)
        {
            if  (_hubConnection.State != HubConnectionState.Disconnected)
                return;

            var random = new Random();
            var delayInterval = 500;
            var attempt = 0;
            while (attempt < 10)
                try {
                    attempt++;
                    if (_hubConnection.State == HubConnectionState.Disconnected)
                        await _hubConnection.StartAsync(cancellationToken);
                    else
                        return;
                }
                catch (OperationCanceledException oce) {
                    throw;
                }
                catch(Exception e) {
                    _logger.LogError(e, "Failed to reconnect SignalR Hub");

                    await Task.Delay(delayInterval, cancellationToken);
                    if (delayInterval < 5000)
                        delayInterval += random.Next(1000);
                }
        }
    }
}
