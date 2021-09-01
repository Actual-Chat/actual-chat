using System.Threading;
using System.Threading.Tasks;
using Stl.CommandR.Configuration;
using Stl.Text;

namespace ActualChat.Audio
{
    public interface IAudioRecorder
    {
        [CommandHandler]
        Task<Symbol> Initialize(InitializeAudioRecorderCommand command, CancellationToken cancellationToken = default);
        [CommandHandler]
        Task Complete(CompleteAudioRecording command, CancellationToken cancellationToken = default);
    }
    
}
