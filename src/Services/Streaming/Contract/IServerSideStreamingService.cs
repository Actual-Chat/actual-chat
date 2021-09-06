using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ActualChat.Streaming
{
    public interface IServerSideStreamingService<TMessage>
    {
        Task PublishStream(StreamId streamId, ChannelReader<TMessage> source, CancellationToken cancellationToken);
        
        Task Publish(StreamId streamId, TMessage message, CancellationToken cancellationToken);
        
        Task Complete(StreamId streamId, CancellationToken cancellationToken);
    }
}