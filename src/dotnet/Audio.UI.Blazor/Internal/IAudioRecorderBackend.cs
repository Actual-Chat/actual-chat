using Microsoft.JSInterop;

namespace ActualChat.Audio.UI.Blazor.Internal
{
    public interface IAudioRecorderBackend
    {
        [JSInvokable]
        void OnStartRecording();
        [JSInvokable]
        void OnAudioData(byte[] chunk);
        [JSInvokable]
        void OnStopRecording();
    }
}
