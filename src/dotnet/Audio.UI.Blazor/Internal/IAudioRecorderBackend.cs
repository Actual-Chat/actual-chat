using System.Threading.Tasks;

namespace ActualChat.Audio.UI.Blazor.Internal
{
    public interface IAudioRecorderBackend
    {
        Task OnStartRecording();
        Task OnAudioData(string dataAsBase64);
        Task OnStopRecording();
    }
}
