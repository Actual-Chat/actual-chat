using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ActualChat.Streaming
{
    public interface IServerSideStreamer<TMessage>
    {
        Task PublishStream(StreamId streamId, ChannelReader<TMessage> content, CancellationToken cancellationToken);
    }
}
