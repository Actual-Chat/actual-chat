namespace ActualChat.App.Maui.Audio.APM;

public sealed class AudioProcessingConfig
{
    private readonly IntPtr _ptr;

    internal AudioProcessingConfig(IntPtr ptr) => _ptr = ptr;

    public AudioProcessingConfig EnableEchoCanceller(bool enabled, bool mobileMode = false)
    {
        NativeMethods.webrtc_apm_config_set_echo_canceller(_ptr, enabled ? 1 : 0, mobileMode ? 1 : 0);
        return this;
    }

    public AudioProcessingConfig EnableNoiseSuppression(bool enabled, NoiseSuppressionLevel level)
    {
        NativeMethods.webrtc_apm_config_set_noise_suppression(_ptr, enabled ? 1 : 0, (int)level);
        return this;
    }

    public AudioProcessingConfig EnableAutomaticGainControl(bool enabled)
    {
        NativeMethods.webrtc_apm_config_set_gain_controller2(_ptr, enabled ? 1 : 0);
        return this;
    }

    internal IntPtr DangerousGetHandle() => _ptr;
}
