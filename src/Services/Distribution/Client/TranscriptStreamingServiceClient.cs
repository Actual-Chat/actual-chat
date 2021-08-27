using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Distribution.Client
{

    public class TranscriptStreamingServiceClient : IStreamingService<TranscriptMessage>
    {
        private readonly HubConnection _hubConnection;

        public TranscriptStreamingServiceClient(HubConnection hubConnection)
        {
            _hubConnection = hubConnection;
            // TODO: AK - We need to initialize hub!!!
        }

        public Task<ChannelReader<TranscriptMessage>> GetStream(string streamId, CancellationToken cancellationToken)
        {
            return _hubConnection.StreamAsChannelCoreAsync<TranscriptMessage>("GetTranscriptStream", new object[] { streamId }, cancellationToken);
        }
    }
}
