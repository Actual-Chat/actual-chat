using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Stl.Text;

namespace ActualChat.Distribution
{
    public interface IAudioStreamingService : IStreamingService<AudioMessage>
    {
        Task<RecordingId> UploadStream(AudioRecordingConfiguration audioConfig, ChannelReader<AudioRecordMessage> source, CancellationToken cancellationToken);
    }
}