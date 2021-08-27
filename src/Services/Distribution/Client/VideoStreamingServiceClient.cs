using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Distribution.Client
{

    public class VideoStreamingServiceClient : IStreamingService<VideoMessage>
    {
        private readonly HubConnection _hubConnection;

        public VideoStreamingServiceClient(HubConnection hubConnection)
        {
            _hubConnection = hubConnection;
            // TODO: AK - We need to initialize hub!!!
        }

        public Task<ChannelReader<VideoMessage>> GetStream(string streamId, CancellationToken cancellationToken)
        {
            return _hubConnection.StreamAsChannelCoreAsync<VideoMessage>("GetVideoStream", new object[] { streamId }, cancellationToken);
        }
    }
}
