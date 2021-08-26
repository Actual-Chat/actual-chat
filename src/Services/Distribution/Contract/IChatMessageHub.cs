using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Stl.Fusion.Authentication;

namespace ActualChat.Distribution
{
    public interface IChatMessageHub
    {
        Task<ChannelReader<MessageVariant>> Subscribe(Session session, string chatId, CancellationToken cancellationToken);
    }
}