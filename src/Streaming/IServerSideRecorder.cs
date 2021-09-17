using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Blobs;
using Stl;

namespace ActualChat.Streaming
{
    public interface IServerSideRecorder<in TRecordId, TRecord>
        where TRecordId : notnull
        where TRecord : class, IHasId<TRecordId>
    {
        // TODO(AY): Won't work in a cluster / multi-host setup, so will require a refactoring
        Task<TRecord?> DequeueNewRecord(CancellationToken cancellationToken);
        Task<ChannelReader<BlobPart>> GetContent(TRecordId recordId, CancellationToken cancellationToken);
    }
}
