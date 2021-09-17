using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ActualChat.Streaming
{
    public interface IStreamer<TMessage>
    {
        Task<ChannelReader<TMessage>> GetStream(StreamId streamId, CancellationToken cancellationToken);
    }
}
