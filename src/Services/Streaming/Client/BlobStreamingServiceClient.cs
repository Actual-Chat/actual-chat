using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Streaming.Client.Module;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Streaming.Client
{
    public sealed class BlobStreamingServiceClient : IStreamingService<BlobMessage>
    {
        private readonly IHubConnectionSentinel _hubConnectionSentinel;

        public BlobStreamingServiceClient(IHubConnectionSentinel hubConnectionSentinel)
        {
            _hubConnectionSentinel = hubConnectionSentinel;
        }

        public async Task<ChannelReader<BlobMessage>> GetStream(StreamId streamId, CancellationToken cancellationToken)
        {
            var hubConnection = await _hubConnectionSentinel.GetInitialized(cancellationToken);
            return await hubConnection.StreamAsChannelCoreAsync<BlobMessage>("GetAudioStream", new object[] { streamId }, cancellationToken);
        }
    }
}
