using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ActualChat.Distribution
{
    [Authorize]
    public class StreamingServiceHub : Hub
    {
        private readonly IAudioStreamingService _audioStreamingService;
        private readonly IStreamingService<VideoMessage> _videoStreamingService;
        private readonly IStreamingService<TranscriptMessage> _transcriptStreamingService;

        public StreamingServiceHub(
            IAudioStreamingService audioStreamingService,
            IStreamingService<VideoMessage> videoStreamingService,
            IStreamingService<TranscriptMessage> transcriptStreamingService)
        {
            _audioStreamingService = audioStreamingService;
            _videoStreamingService = videoStreamingService;
            _transcriptStreamingService = transcriptStreamingService;
        }

        public Task<ChannelReader<AudioMessage>> GetAudioStream(string streamId, CancellationToken cancellationToken)
            => _audioStreamingService.GetStream(streamId, cancellationToken);

        public Task<ChannelReader<VideoMessage>> GetVideoStream(string streamId, CancellationToken cancellationToken)
            => _videoStreamingService.GetStream(streamId, cancellationToken);

        public Task<ChannelReader<TranscriptMessage>> GetTranscriptStream(string streamId, CancellationToken cancellationToken)
            => _transcriptStreamingService.GetStream(streamId, cancellationToken);

        public Task<RecordingId> UploadAudioStream(AudioRecordingConfiguration config, ChannelReader<AudioMessage> source, CancellationToken cancellationToke)
            => _audioStreamingService.UploadStream(config, source, cancellationToke);
    }
}
