using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Stl.Text;

namespace ActualChat.Distribution
{
    public interface IServerSideAudioStreamingService : IServerSideStreamingService<AudioMessage>
    {
        Task<AudioRecording?> WaitForNewRecording(CancellationToken cancellationToken);
        Task<ChannelReader<AudioRecordMessage>> GetStream(RecordingId recordingId, CancellationToken cancellationToken);
    }
}