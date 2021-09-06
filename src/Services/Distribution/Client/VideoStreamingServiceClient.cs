using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Distribution.Client.Module;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Distribution.Client
{
    public class VideoStreamingServiceClient : IStreamingService<VideoMessage>
    {
        private readonly IHubConnectionSentinel _hubConnectionSentinel;

        public VideoStreamingServiceClient(IHubConnectionSentinel hubConnectionSentinel)
        {
            _hubConnectionSentinel = hubConnectionSentinel;
        }

        public async Task<ChannelReader<VideoMessage>> GetStream(StreamId streamId, CancellationToken cancellationToken)
        {
            var hubConnection = await _hubConnectionSentinel.GetInitialized(cancellationToken);
            return await hubConnection.StreamAsChannelCoreAsync<VideoMessage>("GetVideoStream", new object[] { streamId }, cancellationToken);
        }
    }
}
