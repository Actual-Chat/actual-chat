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
        private readonly IAudioStreamReader _audioStreamReader;
        private readonly ITranscriptStreamReader _transcriptStreamReader;
        private readonly IAudioRecorder _audioRecorder;

        public AudioHub(
            IAudioStreamReader audioStreamReader,
            ITranscriptStreamReader transcriptStreamReader,
            IAudioRecorder audioRecorder)
        {
            _audioStreamReader = audioStreamReader;
            _transcriptStreamReader = transcriptStreamReader;
            _audioRecorder = audioRecorder;
        }

        public Task<ChannelReader<BlobPart>> GetAudioStream(string streamId, CancellationToken cancellationToken)
            => _audioStreamReader.GetStream(streamId, cancellationToken);

        public Task<ChannelReader<TranscriptPart>> GetTranscriptStream(string streamId, CancellationToken cancellationToken)
            => _transcriptStreamReader.GetStream(streamId, cancellationToken);

        public Task UploadAudioStream(
            Session session,
            AudioRecord upload,
            ChannelReader<BlobPart> content,
            CancellationToken cancellationToke)
            => _audioRecorder.Record(session, upload, content, cancellationToke);
    }
}
