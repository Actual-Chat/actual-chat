using System.Numerics;

namespace ActualChat.Audio;

/// <summary>
/// Resampler for 48kHz to 16kHz.
/// </summary>
public class Resampler
{
    private readonly float[] _coefficients;
    private readonly int _decimation;
    private readonly int _numTaps;
    private readonly float[] _historyBuffer; // Stateful history for overlapping chunks

    /// <summary>
    /// Initializes the resampler for 48kHz to 16kHz.
    /// </summary>
    /// <param name="numTaps">Number of FIR taps (odd, e.g., 64 for balance of quality/speed).</param>
    public Resampler(int numTaps = 64)
    {
        _numTaps = numTaps;
        _decimation = 3; // 48k / 16k
        _coefficients = DesignLowPassFir(numTaps, 8000.0, 48000.0);
        _historyBuffer = new float[numTaps - 1]; // Initial zeros
    }

    /// <summary>
    /// Calculates the maximum possible output length for a given input length.
    /// Use this to size the output buffer before calling ProcessChunk.
    /// </summary>
    /// <param name="inputLength">Length of the input chunk.</param>
    /// <returns>Maximum output length needed.</returns>
    public int GetMaxOutputLength(int inputLength)
        => (inputLength + _historyBuffer.Length + _decimation - 1) / _decimation;

    /// <summary>
    /// Processes a chunk of input samples (e.g., 20ms at 48kHz = 960 samples for mono).
    /// Writes resampled output to the provided outputBuffer and returns the number of samples written.
    /// Uses spans to minimize allocations and copies. Caller must provide outputBuffer large enough (use GetMaxOutputLength).
    /// </summary>
    /// <param name="inputChunk">Input audio chunk (float32).</param>
    /// <param name="outputBuffer">Pre-allocated buffer to write output to.</param>
    /// <returns>Number of samples written to outputBuffer.</returns>
    public int ProcessChunk(ReadOnlySpan<float> inputChunk, Span<float> outputBuffer)
    {
        int inputLength = inputChunk.Length;
        int maxOutputLength = outputBuffer.Length; // Assume caller sized it correctly
        int outputIdx = 0;

        // Use stackalloc for tempBuffer if small enough; otherwise, rent from ArrayPool if needed.
        // For 20ms chunk (960 samples) + history (~63), total ~1023; safe for stackalloc if numTaps <= ~1000.
        Span<float> tempBuffer = stackalloc float[_historyBuffer.Length + inputLength];
        _historyBuffer.AsSpan().CopyTo(tempBuffer);
        inputChunk.CopyTo(tempBuffer.Slice(_historyBuffer.Length));

        int vectorSize = Vector<float>.Count; // 4 on ARM64 with NEON (128-bit)

        ReadOnlySpan<float> coefficientSpan = _coefficients.AsSpan();

        // Process in steps of decimation
        for (int i = 0; i <= tempBuffer.Length - _numTaps; i += _decimation) {
            float sum = 0f;

            // Vectorized convolution
            int k = 0;
            for (; k + vectorSize <= _numTaps; k += vectorSize) {
                ReadOnlySpan<float> inputSlice = tempBuffer.Slice(i + k, vectorSize);
                ReadOnlySpan<float> coefficientSlice = coefficientSpan.Slice(k, vectorSize);

                var inputVec = new Vector<float>(inputSlice);
                var coefficientVector = new Vector<float>(coefficientSlice);

                sum += Vector.Dot(inputVec, coefficientVector);
            }

            // Scalar remainder
            for (; k < _numTaps; k++)
                sum += tempBuffer[i + k] * _coefficients[k];

            if (outputIdx < maxOutputLength)
                outputBuffer[outputIdx++] = sum;
            else
                // Buffer too small; could throw, but for now, just stop
                break;
        }

        // Update history: last (numTaps - 1) samples of tempBuffer
        int historyStart = tempBuffer.Length - (_numTaps - 1);
        if (historyStart >= 0)
            tempBuffer[historyStart..].CopyTo(_historyBuffer);
        else
        {
            // If chunk too small, shift history
            int overlap = tempBuffer.Length;
            _historyBuffer.AsSpan((_numTaps - 1) - overlap, overlap).CopyTo(_historyBuffer.AsSpan(0, overlap));
            // Note: The original had a bug here; corrected to shift existing history left and append input
            // But since tempBuffer = history + input, and historyStart < 0, we can copy the entire tempBuffer to the end of history
            tempBuffer.CopyTo(_historyBuffer.AsSpan((_numTaps - 1) - inputLength));
        }

        return outputIdx;
    }

    /// <summary>
    /// Designs a low-pass FIR filter using windowed sinc method with Blackman window.
    /// </summary>
    /// <param name="numTaps">Number of filter taps (odd recommended).</param>
    /// <param name="cutoffFreq">Cutoff frequency in Hz.</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <returns>Filter coefficients.</returns>
    private static float[] DesignLowPassFir(int numTaps, double cutoffFreq, double sampleRate)
    {
        double freq = cutoffFreq / sampleRate;
        float[] h = new float[numTaps];
        double delay = (numTaps - 1) / 2.0;
        double sum = 0;

        for (int i = 0; i < numTaps; i++) {
            double diff = i - delay;
            if (Math.Abs(diff) < 1e-10)
                h[i] = (float)(2 * freq);
            else
                h[i] = (float)(Math.Sin(2 * Math.PI * freq * diff) / (Math.PI * diff));

            // Apply Blackman window
            h[i] *= (float)(0.42 - (0.5 * Math.Cos(2 * Math.PI * i / (numTaps - 1))) + (0.08 * Math.Cos(4 * Math.PI * i / (numTaps - 1))));

            sum += h[i];
        }

        // Normalize for unity gain
        if (sum == 0)
            return h;

        for (int i = 0; i < numTaps; i++)
            h[i] /= (float)sum;

        return h;
    }
}
