using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ActualChat.Streaming
{
    [Authorize]
    public class StreamingServiceHub : Hub
    {
        private readonly IStreamingService<BlobMessage> _blobStreamingService;
        private readonly IStreamingService<TranscriptMessage> _transcriptStreamingService;
        private readonly IRecordingService<AudioRecordingConfiguration> _audioRecordingService;

        public StreamingServiceHub(
            IStreamingService<BlobMessage> blobStreamingService,
            IStreamingService<TranscriptMessage> transcriptStreamingService,
            IRecordingService<AudioRecordingConfiguration> audioRecordingService)
        {
            _blobStreamingService = blobStreamingService;
            _transcriptStreamingService = transcriptStreamingService;
            _audioRecordingService = audioRecordingService;
        }

        public Task<ChannelReader<BlobMessage>> GetBlobStream(string streamId, CancellationToken cancellationToken)
            => _blobStreamingService.GetStream(streamId, cancellationToken);

        public Task<ChannelReader<TranscriptMessage>> GetTranscriptStream(string streamId, CancellationToken cancellationToken)
            => _transcriptStreamingService.GetStream(streamId, cancellationToken);

        public Task UploadAudioStream(AudioRecordingConfiguration config, ChannelReader<BlobMessage> source, CancellationToken cancellationToke)
            => _audioRecordingService.UploadRecording(config, source, cancellationToke);
    }
}
