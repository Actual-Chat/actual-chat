using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Distribution.Client.Module;
using Microsoft.AspNetCore.SignalR.Client;

namespace ActualChat.Distribution.Client
{
    public class AudioStreamingServiceClient : IAudioStreamingService
    {
        private readonly IHubConnectionSentinel _hubConnectionSentinel;

        public AudioStreamingServiceClient(IHubConnectionSentinel hubConnectionSentinel)
        {
            _hubConnectionSentinel = hubConnectionSentinel;
        }

        public async Task<ChannelReader<AudioMessage>> GetStream(string streamId, CancellationToken cancellationToken)
        {
            var hubConnection = await _hubConnectionSentinel.GetInitialized(cancellationToken);
            return await hubConnection.StreamAsChannelCoreAsync<AudioMessage>("GetAudioStream", new object[] { streamId }, cancellationToken);
        }

        public async Task<RecordingId> UploadStream(AudioRecordingConfiguration config, ChannelReader<AudioRecordMessage> source, CancellationToken cancellationToken)
        {
            var hubConnection = await _hubConnectionSentinel.GetInitialized(cancellationToken);
            return await hubConnection.InvokeAsync<RecordingId>("UploadAudioStream", config, source, cancellationToken);
        }
    }
}
