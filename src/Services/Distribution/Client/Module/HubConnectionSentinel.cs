using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace ActualChat.Distribution.Client.Module
{
    public class HubConnectionSentinel : IHubConnectionSentinel
    {
        private readonly HubConnection _hubConnection;
        private readonly ILogger<HubConnectionSentinel> _logger;

        public HubConnectionSentinel(HubConnection hubConnection, ILogger<HubConnectionSentinel> logger)
        {
            _hubConnection = hubConnection;
            _logger = logger;
        }

        public async Task<HubConnection> GetInitialized(CancellationToken token)
        {
            if  (_hubConnection.State == HubConnectionState.Disconnected)
                await ConnectWithRetryAsync(token);
            
            return _hubConnection;
        }
        
        private async Task ConnectWithRetryAsync(CancellationToken token)
        {
            var random = new Random();
            var delayInterval = 500;
            var attempt = 0;
            while (attempt < 10)
                try {
                    attempt++;
                    if (_hubConnection.State == HubConnectionState.Disconnected)
                        await _hubConnection.StartAsync(token);
                    else
                        return;
                }
                catch when (token.IsCancellationRequested) {
                    return;
                }
                catch(Exception e)
                {
                    _logger.LogError(e, "Error trying to reconnect the SignalR Hub");
                    
                    await Task.Delay(delayInterval, token);
                    if (delayInterval < 5000)
                        delayInterval += random.Next(1000);
                }
        }
    }
}