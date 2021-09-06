using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Distribution.Client.Module;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Distribution.Client
{
    public class TranscriptStreamingServiceClient : IStreamingService<TranscriptMessage>
    {
        private readonly IHubConnectionSentinel _hubConnectionSentinel;

        public TranscriptStreamingServiceClient(IHubConnectionSentinel hubConnectionSentinel)
        {
            _hubConnectionSentinel = hubConnectionSentinel;
        }

        public async Task<ChannelReader<TranscriptMessage>> GetStream(StreamId streamId, CancellationToken cancellationToken)
        {
            var hubConnection = await _hubConnectionSentinel.GetInitialized(cancellationToken);
            return await hubConnection.StreamAsChannelCoreAsync<TranscriptMessage>("GeTranscriptStream", new object[] { streamId }, cancellationToken);
        }
    }
}
