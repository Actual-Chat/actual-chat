using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Streaming.Client.Module;
using Stl.Fusion.Authentication;

namespace ActualChat.Streaming.Client
{
    public class AudioRecordingServiceClient : IAudioRecordingService
    {
        private readonly IHubConnectionSentinel _hubConnectionSentinel;

        public AudioRecordingServiceClient(IHubConnectionSentinel hubConnectionSentinel)
        {
            _hubConnectionSentinel = hubConnectionSentinel;
        }
        
        public async Task UploadRecording(Session session, string chatId, AudioRecordingConfiguration config, ChannelReader<BlobMessage> source, CancellationToken cancellationToken)
        {
            var hubConnection = await _hubConnectionSentinel.GetInitialized(cancellationToken);
            await hubConnection.SendCoreAsync("UploadAudioStream", new object[]{session, chatId, config, source}, cancellationToken);
        }
    }
}