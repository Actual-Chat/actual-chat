using System.Threading;
using System.Threading.Tasks;
using RestEase;
using Stl.Fusion.Client;
using Stl.Text;

namespace ActualChat.Audio.Client
{
    [BasePath("audio-recorders")]
    public interface IAudioRecorderClientDef
    {
        [Post]
        Task<Symbol> Initialize([Body] InitializeAudioRecorderCommand command, CancellationToken cancellationToken = default);
        
        [Delete]
        Task Complete([Body] CompleteAudioRecording command, CancellationToken cancellationToken = default);
    }
}
