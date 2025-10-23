namespace ActualChat.App.Maui.Audio.APM;

using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public sealed class AudioProcessingModule : IDisposable
{
    private readonly AudioProcessingHandle _apmHandle;
    private readonly AudioProcessingModuleConfig _moduleConfig;
    private readonly ProcessingConfig _streamConfig;
    private bool _disposed;

    public AudioProcessingModule(StreamConfig inputConfig, StreamConfig outputConfig)
    {
        var ptr = NativeMethods.webrtc_apm_create();
        if (ptr == IntPtr.Zero)
            throw StandardError.Configuration("webrtc_apm_create returned null");

        _apmHandle = new AudioProcessingHandle(ptr);
        _moduleConfig = new AudioProcessingModuleConfig();
        _streamConfig = new ProcessingConfig {
            Input = inputConfig,
            Output = inputConfig,
            ReverseInput = outputConfig,
            ReverseOutput = outputConfig,
        };
    }

    public void Configure(Action<AudioProcessingModuleConfig> configure)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AudioProcessingModule));

        configure(_moduleConfig);
        ThrowIfError(NativeMethods.webrtc_apm_apply_config(_apmHandle.DangerousGetHandle(), _moduleConfig.DangerousGetHandle()));
        ThrowIfError(NativeMethods.webrtc_apm_initialize_with_config(
            _apmHandle.DangerousGetHandle(),
            _streamConfig.DangerousGetHandle()));

        // var strPtr = NativeMethods.webrtc_apm_config_dump(_moduleConfig.DangerousGetHandle());
        // string result = Marshal.PtrToStringAnsi(strPtr)!; // Convert IntPtr to C# string
        // Console.WriteLine(result); // Output: Dynamic string from C!
        // NativeMethods.webrtc_apm_string_free(strPtr); // Free the memory allocated in C

    }

    public unsafe void ProcessStream(ReadOnlySpan<float> capture, Span<float> output)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioProcessingModule));
        if (capture.Length != output.Length)
            throw new ArgumentException("Capture and output buffers must match");

        fixed (float* cPtr = capture)
        fixed (float* oPtr = output)
        {
            float** srcPtr = stackalloc float*[1];
            float** destPtr = stackalloc float*[1];
            srcPtr[0] = cPtr;
            destPtr[0] = oPtr;

                ThrowIfError(NativeMethods.webrtc_apm_process_stream(
                    _apmHandle.DangerousGetHandle(),
                    (IntPtr)srcPtr,
                    _streamConfig.GetStreamConfigHandle(ProcessingConfig.StreamIndex.Input),
                    _streamConfig.GetStreamConfigHandle(ProcessingConfig.StreamIndex.Output),
                    (IntPtr)destPtr));
        }
    }

    public unsafe void ProcessReverseStream(ReadOnlySpan<float> render, Span<float> output)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioProcessingModule));
        if (render.Length != output.Length)
            throw new ArgumentException("Render and output buffers must match.");

        fixed (float* rPtr = render)
        fixed (float* oPtr = output)
        {
            float** srcPtr = stackalloc float*[1];
            float** destPtr = stackalloc float*[1];
            srcPtr[0] = rPtr;
            destPtr[0] = oPtr;

                    ThrowIfError(NativeMethods.webrtc_apm_process_reverse_stream(
                        _apmHandle.DangerousGetHandle(),
                        (IntPtr)srcPtr,
                        _streamConfig.GetStreamConfigHandle(ProcessingConfig.StreamIndex.ReverseInput),
                        _streamConfig.GetStreamConfigHandle(ProcessingConfig.StreamIndex.ReverseOutput),
                        (IntPtr)destPtr));
        }
    }

    public unsafe void AnalyzeReverseStream(ReadOnlySpan<float> render)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioProcessingModule));

        fixed (float* rPtr = render)
        {
            float** srcPtr = stackalloc float*[1];
            srcPtr[0] = rPtr;
                ThrowIfError(NativeMethods.webrtc_apm_analyze_reverse_stream(
                    _apmHandle.DangerousGetHandle(),
                    (IntPtr)srcPtr,
                    _streamConfig.GetStreamConfigHandle(ProcessingConfig.StreamIndex.ReverseInput)));
        }
    }

    public void SetDelay(int milliseconds)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AudioProcessingModule));

        NativeMethods.webrtc_apm_set_stream_delay_ms(_apmHandle.DangerousGetHandle(), milliseconds);
    }

    public static int GetFrameSize(int sampleRateHz) =>
        checked((int)NativeMethods.webrtc_apm_get_frame_size(sampleRateHz));

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _apmHandle.DisposeSilently();
        _moduleConfig.DisposeSilently();
        _streamConfig.DisposeSilently();
    }


    // Private methods
    private static void ThrowIfError(int code)
    {
        if (code == 0)
            return;

        throw StandardError.Configuration($"APM call failed with error {code}.");
    }
}

internal sealed class ProcessingConfigHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal ProcessingConfigHandle(IntPtr ptr) : base(true)
        => SetHandle(ptr);

    protected override bool ReleaseHandle()
    {
        NativeMethods.webrtc_apm_processing_config_destroy(handle);
        return true;
    }
}

internal sealed class StreamConfigHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public StreamConfigHandle() : base(true) { }
    public StreamConfigHandle(IntPtr ptr) : base(true) => SetHandle(ptr);

    protected override bool ReleaseHandle()
    {
        NativeMethods.webrtc_apm_stream_config_destroy(handle);
        return true;
    }
}

internal sealed class AudioProcessingHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal AudioProcessingHandle(IntPtr handle) : base(true)
        => SetHandle(handle);

    protected override bool ReleaseHandle()
    {
        NativeMethods.webrtc_apm_destroy(handle);
        return true;
    }
}

internal sealed class AudioProcessingConfigHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal AudioProcessingConfigHandle(IntPtr handle) : base(true)
        => SetHandle(handle);

    protected override bool ReleaseHandle()
    {
        NativeMethods.webrtc_apm_config_destroy(handle);
        return true;
    }
}

internal static partial class NativeMethods
{
    private const string DllName = "webrtc-apm";

    [LibraryImport(DllName)]
    internal static partial IntPtr webrtc_apm_create();

    [LibraryImport(DllName)]
    internal static partial void webrtc_apm_destroy(IntPtr apm);

    [LibraryImport(DllName)]
    internal static partial IntPtr webrtc_apm_config_create();

    [LibraryImport(DllName)]
    internal static partial IntPtr webrtc_apm_config_dump(IntPtr config);

    [LibraryImport(DllName)]
    internal static partial void webrtc_apm_string_free(IntPtr str);

    [LibraryImport(DllName)]
    internal static partial void webrtc_apm_config_destroy(IntPtr config);

    [LibraryImport(DllName)]
    internal static partial void webrtc_apm_config_set_echo_canceller(IntPtr config, int enabled, int mobileMode);

    [LibraryImport(DllName)]
    internal static partial void webrtc_apm_config_set_noise_suppression(IntPtr config, int enabled, int level);

    [LibraryImport(DllName)]
    internal static partial void webrtc_apm_config_set_gain_controller1(
        IntPtr config,
        int enabled,
        GainControlMode gainControlMode,
        int targetLevelDbfs,
        int compressionGainDb,
        int enableLimiter);

    [LibraryImport(DllName)]
    internal static partial void webrtc_apm_config_set_gain_controller2(
        IntPtr config,
        int enabled,
        int enableLimiter,
        int enableInputVolumeController,
        int targetLevelDbfs,
        float maxGainChangeDbPerSecond,
        float headroomDb);

    [LibraryImport(DllName)]
    internal static partial void webrtc_apm_config_set_high_pass_filter(IntPtr config, int enabled);

    [LibraryImport(DllName)]
    internal static partial void webrtc_apm_config_set_pipeline(IntPtr config,
        int maxInternalRate,
        int multiChannelRender,
        int multiChannelCapture,
        DownmixMethod downmixMethod);

    [LibraryImport(DllName)]
    internal static partial int webrtc_apm_apply_config(IntPtr apm, IntPtr config);

    [LibraryImport(DllName)]
    internal static partial int webrtc_apm_initialize(IntPtr apm);

    [LibraryImport(DllName)]
    internal static partial int webrtc_apm_initialize_with_config(IntPtr apm, IntPtr procConfig);

    [LibraryImport(DllName)]
    internal static partial int webrtc_apm_process_stream(
        IntPtr apm,
        IntPtr src,
        IntPtr inputConfig,
        IntPtr outputConfig,
        IntPtr dest);

    [LibraryImport(DllName)]
    internal static partial int webrtc_apm_process_reverse_stream(
        IntPtr apm,
        IntPtr src,
        IntPtr inputConfig,
        IntPtr outputConfig,
        IntPtr dest);

    [LibraryImport(DllName)]
    internal static partial int webrtc_apm_analyze_reverse_stream(
        IntPtr apm,
        IntPtr data,
        IntPtr reverseConfig);

    [LibraryImport(DllName)]
    internal static partial void webrtc_apm_set_stream_delay_ms(IntPtr apm, int delay);

    [LibraryImport(DllName)]
    internal static partial void webrtc_apm_set_runtime_setting_float(IntPtr apm, int type, float value);

    [LibraryImport(DllName)]
    internal static partial void webrtc_apm_set_runtime_setting_int(IntPtr apm, int type, int value);

    [LibraryImport(DllName)]
    internal static partial IntPtr webrtc_apm_stream_config_create(int sampleRateHz, nuint numChannels);

    [LibraryImport(DllName)]
    internal static partial void webrtc_apm_stream_config_destroy(IntPtr config);

    [LibraryImport(DllName)]
    internal static partial IntPtr webrtc_apm_processing_config_create();

    [LibraryImport(DllName)]
    internal static partial void webrtc_apm_processing_config_destroy(IntPtr config);

    [LibraryImport(DllName)]
    internal static partial IntPtr webrtc_apm_processing_config_input_stream(IntPtr config);

    [LibraryImport(DllName)]
    internal static partial IntPtr webrtc_apm_processing_config_output_stream(IntPtr config);

    [LibraryImport(DllName)]
    internal static partial IntPtr webrtc_apm_processing_config_reverse_input_stream(IntPtr config);

    [LibraryImport(DllName)]
    internal static partial IntPtr webrtc_apm_processing_config_reverse_output_stream(IntPtr config);

    [LibraryImport(DllName)]
    internal static partial void webrtc_apm_set_stream_analog_level(IntPtr apm, int level);

    [LibraryImport(DllName)]
    internal static partial int webrtc_apm_recommended_stream_analog_level(IntPtr apm);

    [LibraryImport(DllName)]
    internal static partial void webrtc_apm_set_stream_key_pressed(IntPtr apm, int keyPressed);

    [LibraryImport(DllName)]
    internal static partial void webrtc_apm_set_output_will_be_muted(IntPtr apm, int muted);

    [LibraryImport(DllName)]
    internal static partial int webrtc_apm_proc_sample_rate_hz(IntPtr apm);

    [LibraryImport(DllName)]
    internal static partial int webrtc_apm_proc_split_sample_rate_hz(IntPtr apm);

    [LibraryImport(DllName)]
    internal static partial nuint webrtc_apm_num_input_channels(IntPtr apm);

    [LibraryImport(DllName)]
    internal static partial nuint webrtc_apm_num_proc_channels(IntPtr apm);

    [LibraryImport(DllName)]
    internal static partial nuint webrtc_apm_num_output_channels(IntPtr apm);

    [LibraryImport(DllName)]
    internal static partial nuint webrtc_apm_num_reverse_channels(IntPtr apm);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int webrtc_apm_create_aec_dump(IntPtr apm, string fileName, long maxLogSizeBytes);

    [LibraryImport(DllName)]
    internal static partial void webrtc_apm_detach_aec_dump(IntPtr apm);

    [LibraryImport(DllName)]
    internal static partial nuint webrtc_apm_get_frame_size(int sampleRateHz);
}
