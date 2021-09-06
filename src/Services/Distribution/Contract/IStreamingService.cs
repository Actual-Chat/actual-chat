using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ActualChat.Distribution
{
    public interface IStreamingService<TMessage>
    {
        Task<ChannelReader<TMessage>> GetStream(StreamId streamId, CancellationToken cancellationToken);
    }
}