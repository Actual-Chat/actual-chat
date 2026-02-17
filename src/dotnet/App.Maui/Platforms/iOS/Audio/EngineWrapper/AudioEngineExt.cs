namespace ActualChat.App.Maui.Audio;

public static class AudioEngineExt
{
    public static void StopRecording(this AudioEngine engine)
    {
        engine.Input.Reset();
        engine.Stop();
    }
}
