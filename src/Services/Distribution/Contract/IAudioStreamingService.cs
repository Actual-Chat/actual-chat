using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Stl.Text;

namespace ActualChat.Distribution
{
    public interface IAudioStreamingService : IStreamingService<AudioMessage>
    {
        Task UploadStream(Symbol recordingId, ChannelReader<AudioMessage> source, CancellationToken cancellationToken);
    }
}