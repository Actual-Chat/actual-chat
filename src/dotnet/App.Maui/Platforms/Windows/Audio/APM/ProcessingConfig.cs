namespace ActualChat.App.Maui.Audio.APM;

public sealed class ProcessingConfig : IDisposable
{
    private readonly ProcessingConfigHandle _handle;
    private bool _disposed;

    public ProcessingConfig()
    {
        var ptr = NativeMethods.webrtc_apm_processing_config_create();
        if (ptr == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create processing config.");

        _handle = new ProcessingConfigHandle(ptr);
    }

    public StreamConfig Input {
        get {
            unsafe {
                var nStream = ResolveStream(StreamIndex.Input);
                return new StreamConfig(nStream->SampleRateHz, (int)nStream->NumChannels);
            }
        }
        init {
            unsafe {
                var nStream = ResolveStream(StreamIndex.Input);
                nStream->SampleRateHz = value.SampleRateHz;
                nStream->NumChannels = (nuint)value.Channels;
                nStream->NumFrames = (nuint)AudioProcessingModule.GetFrameSize(value.SampleRateHz);
            }
        }
    }

    public StreamConfig Output {
        get {
            unsafe {
                var nStream = ResolveStream(StreamIndex.Output);
                return new StreamConfig(nStream->SampleRateHz, (int)nStream->NumChannels);
            }
        }
        init {
            unsafe {
                var nStream = ResolveStream(StreamIndex.Output);
                nStream->SampleRateHz = value.SampleRateHz;
                nStream->NumChannels = (nuint)value.Channels;
                nStream->NumFrames = (nuint)AudioProcessingModule.GetFrameSize(value.SampleRateHz);
            }
        }
    }
    public StreamConfig ReverseInput {
        get {
            unsafe {
                var nStream = ResolveStream(StreamIndex.ReverseInput);
                return new StreamConfig(nStream->SampleRateHz, (int)nStream->NumChannels);
            }
        }
        init {
            unsafe {
                var nStream = ResolveStream(StreamIndex.ReverseInput);
                nStream->SampleRateHz = value.SampleRateHz;
                nStream->NumChannels = (nuint)value.Channels;
                nStream->NumFrames = (nuint)AudioProcessingModule.GetFrameSize(value.SampleRateHz);
            }
        }
    }
    public StreamConfig ReverseOutput {
        get {
            unsafe {
                var nStream = ResolveStream(StreamIndex.ReverseOutput);
                return new StreamConfig(nStream->SampleRateHz, (int)nStream->NumChannels);
            }
        }
        init {
            unsafe {
                var nStream = ResolveStream(StreamIndex.ReverseOutput);
                nStream->SampleRateHz = value.SampleRateHz;
                nStream->NumChannels = (nuint)value.Channels;
                nStream->NumFrames = (nuint)AudioProcessingModule.GetFrameSize(value.SampleRateHz);
            }
        }
    }

    internal unsafe IntPtr GetStreamConfigHandle(StreamIndex index)
    {
        var handle = _disposed
            ? throw new ObjectDisposedException(nameof(ProcessingConfig))
            : ResolveStream(index);
        return (IntPtr)handle;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _handle.Dispose();
    }

    private unsafe NativeStreamConfig* ResolveStream(StreamIndex index)
    {
        var wrapper = (NativeProcessingConfigWrapper*)_handle.DangerousGetHandle();
        NativeStreamConfig* basePtr = &wrapper->Input;
        return basePtr + (int)index;
    }

    internal IntPtr DangerousGetHandle() => _handle.DangerousGetHandle();

    internal enum StreamIndex
    {
        Input = 0,
        Output = 1,
        ReverseInput = 2,
        ReverseOutput = 3,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeStreamConfig
    {
        public int SampleRateHz;
        public nuint NumChannels;
        public nuint NumFrames;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeProcessingConfigWrapper
    {
        public NativeStreamConfig Input;
        public NativeStreamConfig Output;
        public NativeStreamConfig ReverseInput;
        public NativeStreamConfig ReverseOutput;
    }
}
