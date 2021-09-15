using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Streaming.Client.Module
{
    public interface IHubConnectionProvider
    {
        Task<HubConnection> GetConnection(CancellationToken cancellationToken);
    }
}
