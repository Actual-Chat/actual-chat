namespace ActualChat.App.Maui.Audio.APM;

public sealed class AudioProcessingModuleConfig : IDisposable
{
    private readonly AudioProcessingConfigHandle _handle;

    public AudioProcessingModuleConfig()
    {
        var cfgPtr = NativeMethods.webrtc_apm_config_create();
        if (cfgPtr == IntPtr.Zero)
            throw StandardError.Configuration("Failed to allocate config");

        _handle = new AudioProcessingConfigHandle(cfgPtr);
    }

    public AudioProcessingModuleConfig EnableEchoCanceller(bool enabled, bool mobileMode = false)
    {
        NativeMethods.webrtc_apm_config_set_echo_canceller(_handle.DangerousGetHandle(), enabled ? 1 : 0, mobileMode ? 1 : 0);
        return this;
    }

    public AudioProcessingModuleConfig EnableNoiseSuppression(bool enabled, NoiseSuppressionLevel level)
    {
        NativeMethods.webrtc_apm_config_set_noise_suppression(_handle.DangerousGetHandle(), enabled ? 1 : 0, (int)level);
        return this;
    }

    public AudioProcessingModuleConfig EnableAutomaticGainControl(bool enabled)
    {
        // NativeMethods.webrtc_apm_config_set_gain_controller(_ptr, enabled ? 1 : 0);
        NativeMethods.webrtc_apm_config_set_gain_controller2(_handle.DangerousGetHandle(), enabled ? 1 : 0);
        return this;
    }

    public AudioProcessingModuleConfig EnableHighPassFilter(bool enabled)
    {
        NativeMethods.webrtc_apm_config_set_high_pass_filter(_handle.DangerousGetHandle(), enabled ? 1 : 0);
        return this;
    }

    public AudioProcessingModuleConfig SetPipeline(bool isMultichannelInput, bool isMultichannelOutput)
    {
        NativeMethods.webrtc_apm_config_set_pipeline(
            _handle.DangerousGetHandle(),
            48000,
            isMultichannelInput ? 1 : 0,
            isMultichannelOutput ? 1 : 0,
            DownmixMethod.Average);
        return this;
    }

    internal IntPtr DangerousGetHandle() => _handle.DangerousGetHandle();

    public void Dispose()
        => _handle.Dispose();
}
