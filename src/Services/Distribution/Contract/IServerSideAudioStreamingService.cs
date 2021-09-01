using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Stl.Text;

namespace ActualChat.Distribution
{
    public interface IServerSideAudioStreamingService : IServerSideStreamingService<AudioMessage>
    {
        Task<ChannelReader<AudioRecordMessage>> GetStream(Symbol recordingId, CancellationToken cancellationToken);
    }
}