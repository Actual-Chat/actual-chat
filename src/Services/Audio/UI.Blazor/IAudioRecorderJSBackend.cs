using System.Threading.Tasks;

namespace ActualChat.Audio.UI.Blazor
{
    public interface IAudioRecorderJSBackend
    {
        Task RecordingStarted();
        Task AudioDataAvailable(string dataAsBase64);
        Task RecordingStopped();
    }
}
