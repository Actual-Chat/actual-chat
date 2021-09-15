using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Streaming.Client.Module;
using Stl.Fusion.Authentication;

namespace ActualChat.Streaming.Client
{
    public class AudioUploaderClient : IAudioUploader
    {
        private readonly IHubConnectionProvider _hubConnectionProvider;

        public AudioUploaderClient(IHubConnectionProvider hubConnectionProvider)
            => _hubConnectionProvider = hubConnectionProvider;

        public async Task Upload(Session session, AudioRecord upload, ChannelReader<BlobPart> content, CancellationToken cancellationToken)
        {
            var hubConnection = await _hubConnectionProvider.GetConnection(cancellationToken);
            await hubConnection.SendCoreAsync("UploadAudioStream", new object[] {session, upload, content}, cancellationToken);
        }
    }
}
