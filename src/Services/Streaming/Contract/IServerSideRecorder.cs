using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ActualChat.Streaming
{
    public interface IServerSideRecorder<TRecording>
        where TRecording : class
    {
        // TODO(AY): Won't work in a cluster / multi-host setup, so will require a refactoring
        Task<TRecording?> WaitForNewRecording(CancellationToken cancellationToken);
        Task<ChannelReader<BlobPart>> GetRecording(AudioRecordId audioRecordId, CancellationToken cancellationToken);
    }
}
