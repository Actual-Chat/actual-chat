namespace ActualChat.App.Maui.Audio;

public static class AudioEngineExt
{
    public static void StopRecording(this AudioEngine engine)
    {
        engine.Input.Reset();
        engine.Stop();
        // On Mac VP must be disarmed between recordings: a lingering Recording-bound
        // VoicePlayer restarts the engine on Play/Resume, and with VP still enabled
        // that would revive the VPIO unit — mic goes live and ducking re-engages.
        if (OperatingSystem.IsMacCatalyst())
            engine.DisableVoiceProcessing();
    }
}
