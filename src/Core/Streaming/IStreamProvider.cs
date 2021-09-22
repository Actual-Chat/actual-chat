using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ActualChat.Streaming
{
    public interface IStreamProvider<in TStreamId, TPart>
        where TStreamId : notnull
    {
        Task<ChannelReader<TPart>> GetStream(TStreamId streamId, CancellationToken cancellationToken);
    }
}
