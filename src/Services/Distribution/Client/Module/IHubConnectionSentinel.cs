using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Distribution.Client.Module
{
    public interface IHubConnectionSentinel
    {
        Task<HubConnection> GetInitialized(CancellationToken token);
    }
}