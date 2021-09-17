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
        private readonly IStreamReader<BlobPart> _blobStreamReader;
        private readonly IStreamReader<TranscriptPart> _transcriptStreamReader;
        private readonly IAudioRecorder _audioRecorder;

        public AudioHub(
            IStreamReader<BlobPart> blobStreamReader,
            IStreamReader<TranscriptPart> transcriptStreamReader,
            IAudioRecorder audioRecorder)
        {
            _blobStreamReader = blobStreamReader;
            _transcriptStreamReader = transcriptStreamReader;
            _audioRecorder = audioRecorder;
        }

        public Task<ChannelReader<BlobPart>> GetBlobStream(string streamId, CancellationToken cancellationToken)
            => _blobStreamReader.GetStream(streamId, cancellationToken);

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
