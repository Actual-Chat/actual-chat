using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Streaming.Client.Module;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Streaming.Client
{
    public class TranscriptStreamerClient : IStreamer<TranscriptPart>
    {
        private readonly IHubConnectionProvider _hubConnectionProvider;

        public TranscriptStreamerClient(IHubConnectionProvider hubConnectionProvider)
            => _hubConnectionProvider = hubConnectionProvider;

        public async Task<ChannelReader<TranscriptPart>> GetStream(StreamId streamId, CancellationToken cancellationToken)
        {
            var hubConnection = await _hubConnectionProvider.GetConnection(cancellationToken);
            return await hubConnection.StreamAsChannelCoreAsync<TranscriptPart>("GeTranscriptStream", new object[] { streamId }, cancellationToken);
        }
    }
}
