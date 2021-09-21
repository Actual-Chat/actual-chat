using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Blobs;
using Stl;

namespace ActualChat.Streaming
{
    public interface IRecordReader<in TRecordId, TRecord>
        where TRecordId : notnull
        where TRecord : class, IHasId<TRecordId>
    {
        Task<TRecord?> DequeueNewRecord(CancellationToken cancellationToken);
        Task<ChannelReader<BlobPart>> GetContent(TRecordId recordId, CancellationToken cancellationToken);
    }
}
