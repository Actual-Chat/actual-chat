using System.Threading;
using System.Threading.Tasks;
using Stl;

namespace ActualChat.Processing
{
    public interface IAsyncProducer<TValue>
    {
        ValueTask<Option<TValue>> TryProduce(CancellationToken cancellationToken);
    }
}
