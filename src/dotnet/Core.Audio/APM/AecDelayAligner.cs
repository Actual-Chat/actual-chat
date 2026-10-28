namespace ActualChat.Audio.APM;

/// <summary>
/// Delays the APM's reverse stream by the measured render-to-capture latency, so the echo lands in
/// the delay range AEC3 searches. A TV fed over HDMI was measured at 580 ms, far past that range.
/// The latency is found by correlating 1 ms energy envelopes of the two streams.
/// </summary>
public sealed class AecDelayAligner
{
    private const int EnvelopeRate = 1000;
    private const double MinCorrelation = 0.3;
    private const double MinReverseLevel = 1e-4;
    private static readonly TimeSpan Margin = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan MaxEstimateDrift = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan MinDelayChange = TimeSpan.FromMilliseconds(30);

    private readonly int _samplesPerEnvelope;
    private readonly int _windowLength;
    private readonly int _maxLag;
    private readonly int _estimatePeriod;
    private readonly float[] _delayLine;
    private readonly double[] _micEnvelope;
    private readonly double[] _reverseEnvelope;
    private long _writtenSampleCount;
    private long _envelopeCount;
    private int _envelopeSampleCount;
    private double _micSum;
    private double _reverseSum;
    private int _lastEstimateLag = -1;

    public int SampleRate { get; }
    public int DelaySamples { get; private set; }
    public TimeSpan Delay => TimeSpan.FromSeconds((double)DelaySamples / SampleRate);
    public TimeSpan LastEstimate { get; private set; }
    public double LastCorrelation { get; private set; }

    public AecDelayAligner(int sampleRate, TimeSpan maxDelay, TimeSpan window, TimeSpan estimatePeriod)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, EnvelopeRate);
        SampleRate = sampleRate;
        _samplesPerEnvelope = sampleRate / EnvelopeRate;
        _windowLength = (int)(window.TotalSeconds * EnvelopeRate);
        _maxLag = (int)(maxDelay.TotalSeconds * EnvelopeRate);
        _estimatePeriod = (int)(estimatePeriod.TotalSeconds * EnvelopeRate);
        _delayLine = new float[(int)(maxDelay.TotalSeconds * sampleRate) + sampleRate];
        _micEnvelope = new double[_windowLength];
        _reverseEnvelope = new double[_windowLength + _maxLag];
    }

    public bool Process(ReadOnlySpan<float> mic, ReadOnlySpan<float> reverse, Span<float> delayedReverse)
    {
        // Returns true when the delay changed, i.e. when the APM's echo path is about to move
        if (mic.Length != reverse.Length || delayedReverse.Length != reverse.Length)
            throw new ArgumentException("Frames must have equal lengths.");

        for (var i = 0; i < reverse.Length; i++) {
            _delayLine[(_writtenSampleCount + i) % _delayLine.Length] = reverse[i];
            var source = _writtenSampleCount + i - DelaySamples;
            delayedReverse[i] = source >= 0 ? _delayLine[source % _delayLine.Length] : 0f;
        }
        _writtenSampleCount += reverse.Length;

        var isChanged = false;
        for (var i = 0; i < mic.Length; i++) {
            _micSum += Math.Abs(mic[i]);
            _reverseSum += Math.Abs(reverse[i]);
            if (++_envelopeSampleCount < _samplesPerEnvelope)
                continue;

            _micEnvelope[_envelopeCount % _micEnvelope.Length] = _micSum / _samplesPerEnvelope;
            _reverseEnvelope[_envelopeCount % _reverseEnvelope.Length] = _reverseSum / _samplesPerEnvelope;
            _envelopeCount++;
            _micSum = _reverseSum = 0;
            _envelopeSampleCount = 0;
            if (_envelopeCount >= _reverseEnvelope.Length && _envelopeCount % _estimatePeriod == 0)
                isChanged |= Estimate();
        }
        return isChanged;
    }

    // Private methods

    private bool Estimate()
    {
        var (lag, correlation) = FindLag();
        LastCorrelation = correlation;
        if (lag < 0) {
            _lastEstimateLag = -1;
            return false;
        }

        LastEstimate = TimeSpan.FromMilliseconds(lag);
        // One peak can be a coincidence of two similar passages; two agreeing ones are the room
        var isConfirmed = _lastEstimateLag >= 0
            && Math.Abs(lag - _lastEstimateLag) <= MaxEstimateDrift.TotalMilliseconds;
        _lastEstimateLag = lag;
        if (!isConfirmed)
            return false;

        var delaySamples = (int)(Math.Max(0, lag - Margin.TotalMilliseconds) * SampleRate / EnvelopeRate);
        if (Math.Abs(delaySamples - DelaySamples) < MinDelayChange.TotalSeconds * SampleRate)
            return false;

        DelaySamples = delaySamples;
        return true;
    }

    private (int Lag, double Correlation) FindLag()
    {
        // Pearson correlation of the latest mic window against the reverse window `lag` ms earlier
        var end = _envelopeCount;
        var n = _windowLength;
        var micMean = 0.0;
        for (var t = end - n; t < end; t++)
            micMean += MicAt(t);
        micMean /= n;
        var micVariance = 0.0;
        for (var t = end - n; t < end; t++)
            micVariance += Square(MicAt(t) - micMean);
        if (micVariance <= 0)
            return (-1, 0);

        var bestLag = -1;
        var bestCorrelation = 0.0;
        for (var lag = 0; lag <= _maxLag; lag++) {
            var reverseMean = 0.0;
            for (var t = end - n; t < end; t++)
                reverseMean += ReverseAt(t - lag);
            reverseMean /= n;
            if (reverseMean < MinReverseLevel)
                continue;

            var covariance = 0.0;
            var reverseVariance = 0.0;
            for (var t = end - n; t < end; t++) {
                var r = ReverseAt(t - lag) - reverseMean;
                covariance += (MicAt(t) - micMean) * r;
                reverseVariance += r * r;
            }
            if (reverseVariance <= 0)
                continue;

            var correlation = covariance / Math.Sqrt(micVariance * reverseVariance);
            if (correlation > bestCorrelation) {
                bestCorrelation = correlation;
                bestLag = lag;
            }
        }
        return bestCorrelation >= MinCorrelation ? (bestLag, bestCorrelation) : (-1, bestCorrelation);
    }

    private double MicAt(long t)
        => _micEnvelope[t % _micEnvelope.Length];

    private double ReverseAt(long t)
        => _reverseEnvelope[t % _reverseEnvelope.Length];

    private static double Square(double x)
        => x * x;
}
