using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ActualChat.Streaming
{
    public interface IStreamPublisher<in TStreamId, TPart>
        where TStreamId : notnull
    {
        Task PublishStream(TStreamId streamId, ChannelReader<TPart> stream, CancellationToken cancellationToken);
    }
}
