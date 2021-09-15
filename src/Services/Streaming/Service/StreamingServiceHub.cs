using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Stl.Fusion.Authentication;

namespace ActualChat.Streaming
{
    [Authorize]
    public class StreamingServiceHub : Hub
    {
        private readonly IStreamer<BlobPart> _blobStreamer;
        private readonly IStreamer<TranscriptPart> _transcriptStreamer;
        private readonly IAudioUploader _audioUploader;

        public StreamingServiceHub(
            IStreamer<BlobPart> blobStreamer,
            IStreamer<TranscriptPart> transcriptStreamer,
            IAudioUploader audioUploader)
        {
            _blobStreamer = blobStreamer;
            _transcriptStreamer = transcriptStreamer;
            _audioUploader = audioUploader;
        }

        public Task<ChannelReader<BlobPart>> GetBlobStream(string streamId, CancellationToken cancellationToken)
            => _blobStreamer.GetStream(streamId, cancellationToken);

        public Task<ChannelReader<TranscriptPart>> GetTranscriptStream(string streamId, CancellationToken cancellationToken)
            => _transcriptStreamer.GetStream(streamId, cancellationToken);

        public Task UploadAudioStream(
            Session session,
            AudioRecord upload,
            ChannelReader<BlobPart> content,
            CancellationToken cancellationToke)
            => _audioUploader.Upload(session, upload, content, cancellationToke);
    }
}
