using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ActualChat.Audio;

public sealed class OnnxVoiceActivityDetector(IServiceProvider services, Func<Task<byte[]>> modelLoader) : VoiceActivityDetector(services)
{
    private InferenceSession? _session;
    private DenseTensor<float> _state = new (new float[2 * 1 * 128], [2, 1, 128]);
    private readonly float[] _context = new float[ContextSamples];

    public override bool IsInitialized => _session != null;

    protected override void Dispose(bool disposing)
    {
        _session?.Dispose();
        _session = null;
        base.Dispose();
    }

    /// <summary>
    ///     Initializes the VAD by loading the ONNX model from the provided file path.
    /// </summary>
    public override async Task EnsureInitialized(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (IsInitialized) {
            Reset();
            return;
        }

        if (modelLoader is null)
            throw StandardError.Configuration("ONNX model file loader is null.");

        var model = await modelLoader().ConfigureAwait(false);
        var options = new SessionOptions();
        options.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount - 1);
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        options.EnableMemoryPattern = true;
        options.EnableCpuMemArena = true;
        options.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_VERBOSE;
        // Configure execution providers depending on platform. We always keep CPU as a fallback.
        try {
            switch (HostInfo.AppKind)
            {
            case AppKind.Android:
                options.AppendExecutionProvider_Nnapi();
                options.AppendExecutionProvider("XNNPACK");
                break;
            case AppKind.Ios or AppKind.MacOS:
                options.AppendExecutionProvider_CoreML(CoreMLFlags.COREML_FLAG_USE_CPU_AND_GPU);
                options.AppendExecutionProvider_CPU();
                break;
            case AppKind.Windows:
                options.AppendExecutionProvider_DML();
                options.AppendExecutionProvider_CPU();
                break;
            default:
                // options.AppendExecutionProvider_DML();
                options.AppendExecutionProvider_CPU();
                // options.AppendExecutionProvider("XNNPACK");
                // options.AppendExecutionProvider_CUDA();
                break;
            }
        }
        catch {
            // If any EP is not supported by the current runtime build, fall back to safe CPU-only.
            Log.LogWarning("Failed to initialize execution providers. Falling back to CPU-only");
            options = new SessionOptions();
            options.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount - 1);
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            options.EnableMemoryPattern = true;
            options.EnableCpuMemArena = true;
            options.AppendExecutionProvider_CPU();
        }

        _session = new InferenceSession(model, options);

        Reset();
    }

    public override void Reset()
    {
        base.Reset();
        Array.Clear(_context, 0, _context.Length);
        _state = new DenseTensor<float>(new float[2 * 1 * 128], [2, 1, 128]);
    }

    protected internal override float[]? AppendChunkInternal(ReadOnlySpan<float> monoPcm)
    {
        ThrowIfDisposed();
        var session = _session;
        if (session == null)
            return null; // Not initialized yet

        if (monoPcm.Length % WindowSamples != 0)
            throw new ArgumentException($"AppendChunkInternal expects N*{WindowSamples} samples.", nameof(monoPcm));

        var frames = monoPcm.Length / WindowSamples;

        // Create tensors: input [B,512], state [2,1,128], context [1,64]
        var inputData = new float[monoPcm.Length];
        monoPcm.CopyTo(inputData);
        var inputTensor = new DenseTensor<float>(inputData, new[] { frames, WindowSamples });
        var stateTensor = _state;
        var contextTensor = new DenseTensor<float>(_context, [1, ContextSamples]);

        // Run inference
        using var results = session.Run([
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
            NamedOnnxValue.CreateFromTensor("state", stateTensor),
            NamedOnnxValue.CreateFromTensor("context", contextTensor),
        ]);

        // Retrieve outputs
        var output = results.First(v => v.Name == "output").AsTensor<float>();
        var stateN = results.First(v => v.Name == "stateN").AsTensor<float>();
        var contextN = results.First(v => v.Name == "contextN").AsTensor<float>();

        UpdateStateFrom(stateN);
        UpdateContextFrom(contextN);

        // Extract probabilities for each frame
        if (output is DenseTensor<float> dOut) {
            var span = dOut.Buffer.Span;
            var probs = new float[Math.Min(frames, dOut.Length)];
            for (int i = 0; i < probs.Length; i++)
                probs[i] = span[i];
            return probs;
        }
        else {
            var probs = new float[frames];
            int i = 0;
            foreach (var v in output) {
                if (i >= frames) break;
                probs[i++] = v;
            }
            return probs;
        }
    }


    private void UpdateStateFrom(Tensor<float> tensor)
    {
        if (tensor is DenseTensor<float> dt) {
            var src = dt.Buffer.Span;
            var dst = _state.Buffer.Span;
            src.CopyTo(dst);
            return;
        }

        // Fallback: enumerate without allocations
        int i = 0;
        foreach (var v in tensor)
            _state.Buffer.Span[i++] = v;
    }

    private void UpdateContextFrom(Tensor<float> tensor)
    {
        if (tensor is DenseTensor<float> dt) {
            var src = dt.Buffer.Span;
            src.CopyTo(_context);
            return;
        }

        // Fallback: enumerate without allocations
        int i = 0;
        foreach (var v in tensor)
            _context[i++] = v;
    }
}
