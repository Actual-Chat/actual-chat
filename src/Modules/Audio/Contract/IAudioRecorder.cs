using System.Threading;
using System.Threading.Tasks;
using Stl.Text;

namespace ActualChat.Audio
{
    public interface IAudioRecorder
    {
        Task<Symbol> Initialize(InitializeAudioRecorderCommand command, CancellationToken cancellationToken = default);
        
        Task AppendAudio(AppendAudioCommand command, Symbol recordingId, CancellationToken cancellationToken = default);
    }

}