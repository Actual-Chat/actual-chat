using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Streaming.Client.Module;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Streaming.Client
{
    public class AudioStreamingServiceClient : IAudioStreamingService
    {
        private readonly IHubConnectionSentinel _hubConnectionSentinel;

        public AudioStreamingServiceClient(IHubConnectionSentinel hubConnectionSentinel)
        {
            _hubConnectionSentinel = hubConnectionSentinel;
        }

        public async Task<ChannelReader<AudioMessage>> GetStream(StreamId streamId, CancellationToken cancellationToken)
        {
            var hubConnection = await _hubConnectionSentinel.GetInitialized(cancellationToken);
            return await hubConnection.StreamAsChannelCoreAsync<AudioMessage>("GetAudioStream", new object[] { streamId }, cancellationToken);
        }

        public async Task UploadRecording(AudioRecordingConfiguration config, ChannelReader<AudioMessage> source, CancellationToken cancellationToken)
        {
            var hubConnection = await _hubConnectionSentinel.GetInitialized(cancellationToken);
            await hubConnection.SendCoreAsync("UploadAudioStream", new object[]{config, source}, cancellationToken);
        }
    }
}
