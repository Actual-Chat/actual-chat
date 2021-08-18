using System.Threading;
using System.Threading.Tasks;

namespace ActualChat.Chat.Markup
{
    public interface IMarkupParser
    {
        public Task<Markup> Parse(string text, CancellationToken cancellationToken = default);
    }
}
