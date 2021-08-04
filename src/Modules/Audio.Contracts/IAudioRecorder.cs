using System.Threading;
using System.Threading.Tasks;
using Stl.Text;

namespace ActualChat.Audio
{
    public interface IAudioRecorder
    {
        Task<Symbol> Initialize(InitializeAudioRecorderCommand audioRecorderCommand, CancellationToken cancellationToken = default);
        
        Task AppendAudio(AppendAudioCommand appendAudioCommand, CancellationToken cancellationToken = default);
    }

}