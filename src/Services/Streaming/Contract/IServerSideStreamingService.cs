using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ActualChat.Streaming
{
    public interface IServerSideStreamingService<TMessage> where TMessage : class, IMessage
    {
        Task PublishStream(StreamId streamId, ChannelReader<TMessage> source, CancellationToken cancellationToken);
    }
}