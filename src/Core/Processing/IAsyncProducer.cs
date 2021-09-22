using System.Threading;
using System.Threading.Tasks;

namespace ActualChat.Processing
{
    public interface IAsyncProducer<TValue>
    {
        ValueTask<TValue> Produce(CancellationToken cancellationToken);
    }
}
