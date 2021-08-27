using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Stl.Fusion.Authentication;

namespace ActualChat.Distribution
{
    public interface IServerSideStreamingService<TMessage>
    {
        Task PublishStream(string streamId, ChannelReader<TMessage> source, CancellationToken cancellationToken);
    }
}