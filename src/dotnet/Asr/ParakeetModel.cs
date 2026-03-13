using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ActualChat.Asr;

/// <summary>
/// Parakeet TDT v3 ASR model using 3 ONNX sessions: preprocessor, encoder, decoder_joint.
/// </summary>
public sealed class ParakeetModel(ILogger? log = null) : IDisposable
{
    private const int SubsamplingFactor = 8;
    private const float WindowStep = 0.01f; // 10ms per frame

    private InferenceSession? _preprocessor;
    private InferenceSession? _encoder;
    private InferenceSession? _decoderJoint;
    private TdtDecoder? _decoder;
    private Vocabulary? _vocab;
    private readonly ILogger _log = log ?? NullLogger.Instance;
    private bool _disposed;

    private static readonly int[] Shape1 = [1];

    public bool IsInitialized => _preprocessor != null;

    /// <summary>Loads the model from file paths.</summary>
    public void Load(ParakeetModelDownloader.ModelFiles modelFiles)
    {
        ThrowIfDisposed();

        _vocab = new Vocabulary(modelFiles.VocabPath);

        // Preprocessor always runs on CPU (contains STFT ops not supported by DirectML)
        var cpuOptions = CreateSessionOptions(cpuOnly: true);
        _preprocessor = new InferenceSession(modelFiles.PreprocessorPath, cpuOptions);

        // Encoder + Decoder can use GPU
        var gpuOptions = CreateSessionOptions(cpuOnly: false);
        _encoder = new InferenceSession(modelFiles.EncoderPath, gpuOptions);
        _decoderJoint = new InferenceSession(modelFiles.DecoderJointPath, gpuOptions);

        _decoder = new TdtDecoder(
            _decoderJoint,
            _vocab.VocabSize,
            _vocab.BlankIndex);

        _log.LogInformation("Parakeet model loaded (vocab: {VocabSize}, blank: {BlankIdx})",
            _vocab.VocabSize, _vocab.BlankIndex);
    }

    /// <summary>Transcribes audio samples (float32, 16kHz mono).</summary>
    public TranscriptionResult Transcribe(float[] audio16kHz)
    {
        ThrowIfDisposed();
        if (_preprocessor == null || _encoder == null || _decoder == null || _vocab == null)
            throw new InvalidOperationException("Model not initialized. Call Load() first.");

        if (audio16kHz.Length < 400) // Need at least one STFT frame
            return new TranscriptionResult("", []);

        // Step 1: Preprocessor - PCM -> mel features
        var (features, featuresLen) = RunPreprocessor(audio16kHz);

        // Step 2: Encoder - mel features -> encoded
        var (encoderOutput, encoderLength, hiddenDim) = RunEncoder(features, featuresLen);

        // Step 3: TDT decoding
        var result = _decoder.Decode(encoderOutput, encoderLength, hiddenDim);

        // Step 4: Convert to text with timestamps
        var timeScale = WindowStep * SubsamplingFactor;
        var words = _vocab.DecodeTokensWithTimestamps(result.TokenIds, result.Timestamps, timeScale);
        var text = _vocab.DecodeTokens(result.TokenIds);

        return new TranscriptionResult(text, words);
    }

    private (float[] features, int featuresLen) RunPreprocessor(float[] audio)
    {
        var waveformLen = audio.Length;
        var waveformTensor = new DenseTensor<float>(audio, new[] { 1, waveformLen });
        var waveformLensValue = CreateIntInput(_preprocessor!, "waveforms_lens", new[] { waveformLen }, Shape1);

        using var results = _preprocessor!.Run([
            NamedOnnxValue.CreateFromTensor("waveforms", waveformTensor),
            waveformLensValue,
        ]);

        var featuresTensor = GetOutput<float>(results, "features");

        // features shape: [1, 128, T]
        var features = CopyToArray(featuresTensor);
        var featuresLen = GetIntOutput(results, "features_lens");

        return (features, featuresLen);
    }

    private (float[] encoderOutput, int encoderLength, int hiddenDim) RunEncoder(float[] features, int featuresLen)
    {
        // features is [1, 128, T] from preprocessor
        // encoder expects: audio_signal [batch, 128, T], length [batch]
        var featuresDims = GetDimensionsFromFlatArray(features.Length, 128);
        var featuresTensor = new DenseTensor<float>(features, new[] { 1, 128, featuresDims });
        var lengthValue = CreateIntInput(_encoder!, "length", new[] { featuresLen }, Shape1);

        using var results = _encoder!.Run([
            NamedOnnxValue.CreateFromTensor("audio_signal", featuresTensor),
            lengthValue,
        ]);

        var outputsTensor = GetOutput<float>(results, "outputs");

        // outputs shape: [batch, hidden_dim, T'] - need to transpose to [batch, T', hidden_dim]
        var dims = outputsTensor.Dimensions.ToArray();
        var hiddenDim = dims[1];
        var encodedTimeSteps = dims[2];
        var encoderLength = GetIntOutput(results, "encoded_lengths");

        // Transpose from [1, hidden_dim, T'] to [T', hidden_dim]
        var rawOutput = CopyToArray(outputsTensor);
        var transposed = new float[encodedTimeSteps * hiddenDim];
        for (int t = 0; t < encodedTimeSteps; t++) {
            for (int h = 0; h < hiddenDim; h++) {
                transposed[t * hiddenDim + h] = rawOutput[h * encodedTimeSteps + t];
            }
        }

        return (transposed, encoderLength, hiddenDim);
    }

    private static int GetDimensionsFromFlatArray(int totalLength, int featureSize)
        => totalLength / featureSize;

    /// <summary>
    /// Creates a NamedOnnxValue with int32 or int64 tensor based on model input metadata.
    /// </summary>
    private static NamedOnnxValue CreateIntInput(InferenceSession session, string name, int[] values, int[] shape)
    {
        var meta = session.InputMetadata[name];
        if (meta.ElementDataType == TensorElementType.Int32) {
            return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<int>(values, shape));
        }
        var longValues = Array.ConvertAll(values, v => (long)v);
        return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(longValues, shape));
    }

    /// <summary>
    /// Reads a scalar integer output, handling both int32 and int64 tensor types.
    /// </summary>
    private static int GetIntOutput(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, string name)
    {
        foreach (var r in results) {
            if (!string.Equals(r.Name, name))
                continue;

            var value = r.Value;
            if (value is DenseTensor<int> intTensor)
                return intTensor.Buffer.Span[0];
            if (value is DenseTensor<long> longTensor)
                return (int)longTensor.Buffer.Span[0];

            // Fallback: try via ElementType on OrtValue
            try { return (int)((DenseTensor<long>)r.AsTensor<long>()).Buffer.Span[0]; }
            catch { /* not long */ }
            try { return ((DenseTensor<int>)r.AsTensor<int>()).Buffer.Span[0]; }
            catch { /* not int */ }

            throw new InvalidOperationException($"Output '{name}' has unexpected element type.");
        }
        throw new InvalidOperationException($"Output '{name}' not found.");
    }

    private static Tensor<T> GetOutput<T>(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, string name)
        where T : unmanaged
    {
        foreach (var r in results) {
            if (string.Equals(r.Name, name))
                return r.AsTensor<T>();
        }
        throw new InvalidOperationException($"Output '{name}' not found.");
    }

    private static float[] CopyToArray(Tensor<float> tensor)
    {
        var result = new float[tensor.Length];
        if (tensor is DenseTensor<float> dense) {
            dense.Buffer.Span.CopyTo(result);
        }
        else {
            int i = 0;
            foreach (var v in tensor)
                result[i++] = v;
        }
        return result;
    }

    private static SessionOptions CreateSessionOptions(bool cpuOnly)
    {
        var options = new SessionOptions();
        options.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount - 1);
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        options.EnableMemoryPattern = true;
        options.EnableCpuMemArena = true;

        if (cpuOnly) {
            options.AppendExecutionProvider_CPU();
            return options;
        }

        try {
            if (OperatingSystem.IsWindows()) {
                options.AppendExecutionProvider_DML();
            }
            options.AppendExecutionProvider_CPU();
        }
        catch {
            // Fallback to CPU if DirectML is unavailable
            options = new SessionOptions();
            options.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount - 1);
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            options.EnableMemoryPattern = true;
            options.EnableCpuMemArena = true;
            options.AppendExecutionProvider_CPU();
        }

        return options;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _decoderJoint?.Dispose();
        _encoder?.Dispose();
        _preprocessor?.Dispose();
    }
}
