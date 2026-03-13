using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ActualChat.Asr;

/// <summary>
/// TDT (Token-and-Duration Transducer) greedy decoder.
/// Ported from onnx-asr Python: _AsrWithTransducerDecoding._decoding + NemoConformerTdt._decode.
/// </summary>
public sealed class TdtDecoder
{
    private readonly InferenceSession _decoderJoint;
    private readonly int _vocabSize;
    private readonly int _blankIndex;
    private readonly int _maxTokensPerStep;
    private readonly int _stateDim1;
    private readonly int _stateDim2;
    private readonly int _stateLayers1;
    private readonly int _stateLayers2;

    // Pre-allocated shape arrays to avoid CA1861 in hot path
    private static readonly int[] Shape1x1 = [1, 1];
    private static readonly int[] Shape1 = [1];
    private static readonly int[] Value1 = [1];

    public TdtDecoder(
        InferenceSession decoderJoint,
        int vocabSize,
        int blankIndex,
        int maxTokensPerStep = 10)
    {
        _decoderJoint = decoderJoint;
        _vocabSize = vocabSize;
        _blankIndex = blankIndex;
        _maxTokensPerStep = maxTokensPerStep;

        // Read state dimensions from ONNX model metadata
        var inputs = _decoderJoint.InputMetadata;
        var state1Shape = inputs["input_states_1"].Dimensions;
        var state2Shape = inputs["input_states_2"].Dimensions;
        _stateLayers1 = state1Shape[0];
        _stateDim1 = state1Shape[2];
        _stateLayers2 = state2Shape[0];
        _stateDim2 = state2Shape[2];
    }

    public record DecodingResult(
        IReadOnlyList<int> TokenIds,
        IReadOnlyList<int> Timestamps);

    /// <summary>
    /// Runs greedy TDT decoding over encoder output.
    /// </summary>
    /// <param name="encoderOutput">Encoder output, shape [T, hidden_dim] (already transposed).</param>
    /// <param name="encoderLength">Number of valid encoder timesteps.</param>
    public DecodingResult Decode(float[] encoderOutput, int encoderLength, int hiddenDim)
    {
        var tokens = new List<int>();
        var timestamps = new List<int>();

        // Create initial decoder state (zeros)
        var state1 = new float[_stateLayers1 * 1 * _stateDim1];
        var state2 = new float[_stateLayers2 * 1 * _stateDim2];

        int t = 0;
        int emittedTokens = 0;

        while (t < encoderLength) {
            // Extract encoder_out[t] as [1, hidden_dim, 1]
            var encoderFrame = new float[hiddenDim];
            Array.Copy(encoderOutput, (long)t * hiddenDim, encoderFrame, 0, hiddenDim);

            // Get previous token (or blank for first)
            int prevToken = tokens.Count > 0 ? tokens[^1] : _blankIndex;

            // Run decoder_joint
            var (output, newState1, newState2) = RunDecoderJoint(
                encoderFrame, hiddenDim, prevToken, state1, state2);

            // TDT: split output into vocab logits + duration logits
            // output shape: [vocab_size + num_durations]
            var tokenLogits = output.AsSpan(0, _vocabSize);
            var durationLogits = output.AsSpan(_vocabSize);

            // Greedy: argmax over token logits
            int token = ArgMax(tokenLogits);

            // Greedy: argmax over duration logits -> step
            int step = ArgMax(durationLogits);

            if (token != _blankIndex) {
                state1 = newState1;
                state2 = newState2;
                tokens.Add(token);
                timestamps.Add(t);
                emittedTokens++;
            }

            if (step > 0) {
                t += step;
                emittedTokens = 0;
            }
            else if (token == _blankIndex || emittedTokens == _maxTokensPerStep) {
                t += 1;
                emittedTokens = 0;
            }
        }

        return new DecodingResult(tokens, timestamps);
    }

    private (float[] output, float[] state1, float[] state2) RunDecoderJoint(
        float[] encoderFrame, int hiddenDim, int prevToken, float[] state1, float[] state2)
    {
        // encoder_outputs: [1, hidden_dim, 1] - matches Python: encoder_out[None, :, None]
        var encoderTensor = new DenseTensor<float>(encoderFrame, new[] { 1, hiddenDim, 1 });

        // targets: [[prevToken]] - model may expect int32 or int64
        var targetsValue = CreateIntTensor("targets", new[] { prevToken }, Shape1x1);

        // target_length: [1]
        var targetLenValue = CreateIntTensor("target_length", Value1, Shape1);

        // input_states
        var state1Tensor = new DenseTensor<float>(state1, new[] { _stateLayers1, 1, _stateDim1 });
        var state2Tensor = new DenseTensor<float>(state2, new[] { _stateLayers2, 1, _stateDim2 });

        var inputs = new List<NamedOnnxValue> {
            NamedOnnxValue.CreateFromTensor("encoder_outputs", encoderTensor),
            targetsValue,
            targetLenValue,
            NamedOnnxValue.CreateFromTensor("input_states_1", state1Tensor),
            NamedOnnxValue.CreateFromTensor("input_states_2", state2Tensor),
        };

        using var results = _decoderJoint.Run(inputs);

        // Extract outputs
        var outputTensor = GetTensorOutput<float>(results, "outputs");
        var outState1 = GetTensorOutput<float>(results, "output_states_1");
        var outState2 = GetTensorOutput<float>(results, "output_states_2");

        // Squeeze output to 1D
        var output = new float[outputTensor.Length];
        CopyTensorToArray(outputTensor, output);

        var newState1 = new float[outState1.Length];
        CopyTensorToArray(outState1, newState1);

        var newState2 = new float[outState2.Length];
        CopyTensorToArray(outState2, newState2);

        return (output, newState1, newState2);
    }

    private static Tensor<T> GetTensorOutput<T>(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, string name)
        where T : unmanaged
    {
        foreach (var r in results) {
            if (string.Equals(r.Name, name))
                return r.AsTensor<T>();
        }
        throw new InvalidOperationException($"Output '{name}' not found in decoder_joint results.");
    }

    private static void CopyTensorToArray<T>(Tensor<T> tensor, T[] array) where T : unmanaged
    {
        if (tensor is DenseTensor<T> dense) {
            dense.Buffer.Span.CopyTo(array);
        }
        else {
            int i = 0;
            foreach (var v in tensor)
                array[i++] = v;
        }
    }

    /// <summary>
    /// Creates a NamedOnnxValue with int32 or int64 tensor depending on model metadata.
    /// </summary>
    private NamedOnnxValue CreateIntTensor(string name, int[] values, int[] shape)
    {
        var meta = _decoderJoint.InputMetadata[name];
        if (meta.ElementDataType == TensorElementType.Int32) {
            var tensor = new DenseTensor<int>(values, shape);
            return NamedOnnxValue.CreateFromTensor(name, tensor);
        }
        // Default to long
        var longValues = Array.ConvertAll(values, v => (long)v);
        var longTensor = new DenseTensor<long>(longValues, shape);
        return NamedOnnxValue.CreateFromTensor(name, longTensor);
    }

    private static int ArgMax(ReadOnlySpan<float> values)
    {
        int bestIdx = 0;
        float bestVal = values[0];
        for (int i = 1; i < values.Length; i++) {
            if (values[i] > bestVal) {
                bestVal = values[i];
                bestIdx = i;
            }
        }
        return bestIdx;
    }
}
