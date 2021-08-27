using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Distribution.Client
{

    public class AudioStreamingServiceClient : IStreamingService<AudioMessage>
    {
        private readonly HubConnection _hubConnection;

        public AudioStreamingServiceClient(HubConnection hubConnection)
        {
            _hubConnection = hubConnection;
            // TODO: AK - We need to initialize hub!!!
        }

        public Task<ChannelReader<AudioMessage>> GetStream(string streamId, CancellationToken cancellationToken)
        {
            return _hubConnection.StreamAsChannelCoreAsync<AudioMessage>("GetAudioStream", new object[] { streamId }, cancellationToken);
        }
    }
}
