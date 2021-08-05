using System.Threading;
using System.Threading.Tasks;
using RestEase;
using Stl.Fusion.Client;
using Stl.Text;

namespace ActualChat.Audio.Client
{
    [RegisterRestEaseReplicaService(typeof(IAudioRecorder))]
    [BasePath("audio-recorders")]
    public interface IAudioRecorderClientDef
    {
        [Post]
        Task<Symbol> Initialize([Body] InitializeAudioRecorderCommand command, CancellationToken cancellationToken = default);
        
        [Post("append")]
        Task AppendAudio([Body] AppendAudioCommand command, CancellationToken cancellationToken = default);
    }
}