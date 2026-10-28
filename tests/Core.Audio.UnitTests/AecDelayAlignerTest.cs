using ActualChat.Audio.APM;

namespace Core.Audio.UnitTests;

public sealed class AecDelayAlignerTest(ITestOutputHelper @out) : TestBase(@out)
{
    private const int SampleRate = 16000;
    private const int FrameLength = SampleRate / 100;

    [Fact]
    public void ShouldDelayTheReverseStreamByTheMeasuredLatency()
    {
        // arrange
        var reverse = CreateBurstySignal(SampleRate * 12, seed: 1);
        var mic = Delay(reverse, SampleRate * 580 / 1000, 0.3f);
        AddNoise(mic, 0.002f, seed: 2);
        var aligner = CreateAligner();

        // act
        Run(aligner, mic, reverse);

        // assert
        aligner.LastEstimate.TotalMilliseconds.Should().BeApproximately(580, 5);
        aligner.Delay.TotalMilliseconds.Should().BeApproximately(530, 5,
            "the delay stays a margin short of the latency, leaving AEC3 its own search range");
    }

    [Fact]
    public void ShouldKeepTheReverseStreamUndelayedWithoutAnEcho()
    {
        // arrange
        var reverse = CreateBurstySignal(SampleRate * 12, seed: 1);
        var mic = CreateBurstySignal(SampleRate * 12, seed: 3);
        var aligner = CreateAligner();

        // act
        Run(aligner, mic, reverse);

        // assert
        aligner.DelaySamples.Should().Be(0);
    }

    [Fact]
    public void ShouldOutputTheReverseStreamShiftedByTheDelay()
    {
        // arrange
        var reverse = CreateBurstySignal(SampleRate * 12, seed: 1);
        var mic = Delay(reverse, SampleRate * 300 / 1000, 0.3f);
        var aligner = CreateAligner();

        // act
        var delayed = Run(aligner, mic, reverse);

        // assert
        var delaySamples = aligner.DelaySamples;
        delaySamples.Should().BePositive();
        var tail = delayed.AsSpan(delayed.Length - SampleRate);
        var expected = reverse.AsSpan(reverse.Length - SampleRate - delaySamples, SampleRate);
        tail.SequenceEqual(expected).Should().BeTrue("the output is the reverse stream as it was the delay earlier");
    }

    // Private methods

    private static AecDelayAligner CreateAligner()
        => new(SampleRate, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1));

    private static float[] Run(AecDelayAligner aligner, float[] mic, float[] reverse)
    {
        var delayed = new float[reverse.Length];
        for (var offset = 0; offset + FrameLength <= reverse.Length; offset += FrameLength)
            aligner.Process(
                mic.AsSpan(offset, FrameLength),
                reverse.AsSpan(offset, FrameLength),
                delayed.AsSpan(offset, FrameLength));
        return delayed;
    }

    private static float[] CreateBurstySignal(int length, int seed)
    {
        // Noise switched on and off at random, so the energy envelope has a shape to match
        var random = new Random(seed);
        var result = new float[length];
        var level = 0f;
        for (var i = 0; i < length; i++) {
            if (i % (SampleRate / 20) == 0)
                level = random.NextDouble() < 0.5 ? 0f : (float)random.NextDouble() * 0.5f;
            result[i] = level * (float)(random.NextDouble() * 2 - 1);
        }
        return result;
    }

    private static float[] Delay(float[] source, int delaySamples, float gain)
    {
        var result = new float[source.Length];
        for (var i = delaySamples; i < source.Length; i++)
            result[i] = source[i - delaySamples] * gain;
        return result;
    }

    private static void AddNoise(float[] target, float level, int seed)
    {
        var random = new Random(seed);
        for (var i = 0; i < target.Length; i++)
            target[i] += level * (float)(random.NextDouble() * 2 - 1);
    }
}
