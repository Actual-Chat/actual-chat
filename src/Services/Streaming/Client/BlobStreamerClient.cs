using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Streaming.Client.Module;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Streaming.Client
{
    public sealed class BlobStreamerClient : IStreamer<BlobPart>
    {
        private readonly IHubConnectionProvider _hubConnectionProvider;

        public BlobStreamerClient(IHubConnectionProvider hubConnectionProvider)
            => _hubConnectionProvider = hubConnectionProvider;

        public async Task<ChannelReader<BlobPart>> GetStream(StreamId streamId, CancellationToken cancellationToken)
        {
            var hubConnection = await _hubConnectionProvider.GetConnection(cancellationToken);
            return await hubConnection.StreamAsChannelCoreAsync<BlobPart>("GetAudioStream", new object[] { streamId }, cancellationToken);
        }
    }
}
