using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ActualChat.Streaming
{
    public interface IServerSideRecordingService<TRecording> where TRecording : class, IRecording
    {
        Task<TRecording?> WaitForNewRecording(CancellationToken cancellationToken);
        Task<ChannelReader<BlobMessage>> GetRecording(RecordingId recordingId, CancellationToken cancellationToken);
    }
}