using System.Threading;
using System.Threading.Tasks;
using Stl.CommandR.Configuration;

namespace ActualChat.Blobs
{
    public interface IBlobWriter
    {
        [CommandHandler]
        Task Append(BlobAppendCommand command, CancellationToken cancellationToken = default);
        [CommandHandler]
        Task Remove(BlobRemoveCommand command, CancellationToken cancellationToken = default);
    }
}
