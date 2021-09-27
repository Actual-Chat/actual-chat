using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Blobs;
using ActualChat.Streaming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Stl.Fusion.Authentication;

namespace ActualChat.Audio
{
    [Authorize]
    public class AudioHub : Hub
    {
        private readonly IAudioStreamProvider _audioStreamProvider;
        private readonly ITranscriptStreamProvider _transcriptStreamProvider;
        private readonly IAudioRecorder _audioRecorder;

        public AudioHub(
            IAudioStreamProvider audioStreamProvider,
            ITranscriptStreamProvider transcriptStreamProvider,
            IAudioRecorder audioRecorder)
        {
            _audioStreamProvider = audioStreamProvider;
            _transcriptStreamProvider = transcriptStreamProvider;
            _audioRecorder = audioRecorder;
        }

        public Task<ChannelReader<BlobPart>> GetAudioStream(string streamId, CancellationToken cancellationToken)
            => _audioStreamProvider.GetStream(streamId, cancellationToken);

        public Task<ChannelReader<TranscriptPart>> GetTranscriptStream(string streamId, CancellationToken cancellationToken)
            => _transcriptStreamProvider.GetStream(streamId, cancellationToken);

        public Task UploadAudioStream(
            Session session,
            AudioRecord upload,
            ChannelReader<BlobPart> content,
            CancellationToken cancellationToke)
            => _audioRecorder.Record(session, upload, content, cancellationToke);
    }
}
