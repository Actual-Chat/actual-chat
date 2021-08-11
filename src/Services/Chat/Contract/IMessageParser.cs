using System.Threading;
using System.Threading.Tasks;
using Stl.Fusion;

namespace ActualChat.Chat
{
    public interface IMessageParser
    {
        [ComputeMethod(KeepAliveTime = 1)]
        public Task<ParsedMessage> Parse(string text, CancellationToken cancellationToken = default);
    }
}
