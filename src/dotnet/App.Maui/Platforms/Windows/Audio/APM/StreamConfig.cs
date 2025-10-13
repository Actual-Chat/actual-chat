namespace ActualChat.App.Maui.Audio.APM;

public readonly record struct StreamConfig(int SampleRateHz, int Channels)
{
    internal StreamConfigHandle ToNative()
    {
        var ptr = NativeMethods.webrtc_apm_stream_config_create(SampleRateHz, (nuint)Channels);
        return ptr == IntPtr.Zero
            ? throw StandardError.Configuration("Failed to create stream config")
            : new StreamConfigHandle(ptr);
    }
}
