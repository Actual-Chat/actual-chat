using ActualChat.Hosting;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ActualChat.App.Maui.Services.Recording;

public sealed class OnnxVoiceActivityDetector(IServiceProvider services) : VoiceActivityDetector(services)
{
    private InferenceSession? _session;
    private DenseTensor<float> _state = new (new float[2 * 1 * 128], [2, 1, 128]);

    public override bool IsInitialized => _session != null;

    public override void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }
    /// <summary>
    ///     Initializes the VAD by loading the ONNX model from the app package.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    public override async Task EnsureInitialized(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_session != null)
            return;

        // The model is shipped under wwwroot/dist/assets/onnx/vad.onnx (see web build assets)
        await using var modelStream = await FileSystem.OpenAppPackageFileAsync(@"wwwroot\dist\assets\onnx\vad.onnx");
        using var ms = new MemoryStream();
        await modelStream.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;

        var options = new SessionOptions();
        options.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount - 1);
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        options.EnableMemoryPattern = true;
        options.EnableCpuMemArena = true;
        // Configure execution providers depending on platform. We always keep CPU as a fallback.
        try {
            switch (HostInfo.AppKind)
            {
            case AppKind.Android:
                // Prefer NNAPI if available. Note: calling order matters, last appended has highest priority.
                options.AppendExecutionProvider_CPU();
                options.AppendExecutionProvider_Nnapi();
                break;
            case AppKind.Ios or AppKind.MacOS:
                // Prefer CoreML on iOS with CPU fallback.
                options.AppendExecutionProvider_CPU();
                options.AppendExecutionProvider_CoreML(CoreMLFlags.COREML_FLAG_USE_CPU_AND_GPU);
                break;
            case AppKind.Windows:
                // Windows: prefer DirectML if available (GPU), then CPU.
                options.AppendExecutionProvider_CPU();
                options.AppendExecutionProvider_DML();
                break;
            default:
                // Other platforms (including Wasm): CPU only.
                options.AppendExecutionProvider_CPU();
                break;
            }
        }
        catch {
            // If any EP is not supported by the current runtime build, fall back to safe CPU-only.
            options = new SessionOptions();
            options.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount - 1);
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            options.EnableMemoryPattern = true;
            options.EnableCpuMemArena = true;
            options.AppendExecutionProvider_CPU();
        }

        _session = new InferenceSession(ms.ToArray(), options);

        ResetProcessingState();
    }

    public override void Reset()
    {
        base.Reset();
        _state = new DenseTensor<float>(new float[2 * 1 * 128], [2, 1, 128]);
    }

    protected override float? AppendChunkInternal(ReadOnlySpan<float> monoPcm)
    {
        ThrowIfDisposed();
        var session = _session;
        if (session == null)
            return null; // Not initialized yet

        if (monoPcm.Length != WindowSamples)
            throw new ArgumentException($"AppendChunkInternal expects exactly {WindowSamples} samples.", nameof(monoPcm));

        // Prepare input buffer: [context | current window]
        Array.Copy(_context,
            0,
            _buffer,
            0,
            ContextSamples);
        monoPcm.CopyTo(_buffer.AsSpan(ContextSamples));

        // Create tensors
        var inputTensor = new DenseTensor<float>(_buffer, [1, InputSamples]);
        var stateTensor = _state;

        // Run inference
        using var results = session.Run([
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
            NamedOnnxValue.CreateFromTensor("state", stateTensor),
        ]);

        // Retrieve outputs
        var output = results.First(v => v.Name == "output").AsTensor<float>();
        var stateN = results.First(v => v.Name == "stateN").AsTensor<float>();

        // Update recurrent state and rolling context
        _state = ToDense(stateN);
        monoPcm.Slice(monoPcm.Length - ContextSamples, ContextSamples).CopyTo(_context);

        var prob = output.Length == 0 ? 0f : output[0];
        return prob;
    }

    private static DenseTensor<float> ToDense(Tensor<float> tensor)
    {
        if (tensor is DenseTensor<float> dense)
            return dense;

        var arr = tensor.ToArray();
        return new DenseTensor<float>(arr, tensor.Dimensions.ToArray());
    }
}
