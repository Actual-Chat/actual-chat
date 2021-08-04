using System.Threading.Tasks;

namespace ActualChat.Audio.UI.Blazor
{
    public interface IAudioRecorderBackend
    {
        Task RecordingStarted();

        Task AudioDataAvailable(string dataAsBase64);

        Task RecordingStopped();
    }
}