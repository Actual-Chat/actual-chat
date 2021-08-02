using System.Threading;
using System.Threading.Tasks;

namespace ActualChat.Hosting
{
    public interface IDataInitializer
    {
        Task Initialize(bool recreate, CancellationToken cancellationToken = default);
    }
}
