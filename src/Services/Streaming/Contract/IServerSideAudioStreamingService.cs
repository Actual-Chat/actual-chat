using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ActualChat.Streaming
{
    public interface IServerSideAudioStreamingService : IServerSideStreamingService<AudioMessage>
    {
        Task<AudioRecording?> WaitForNewRecording(CancellationToken cancellationToken);
        Task<ChannelReader<AudioMessage>> GetRecording(RecordingId recordingId, CancellationToken cancellationToken);
    }
}