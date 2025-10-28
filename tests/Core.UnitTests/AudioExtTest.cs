namespace ActualChat.Core.UnitTests;

public class AudioExtTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void ApproximateGain_ReturnsZero_OnEmpty()
    {
        var data = ReadOnlySpan<float>.Empty;
        var g = AudioExt.ApproximateGain(data);
        g.Should().Be(0);
    }


    [Fact]
    public void Both_ReturnOne_OnAllOnes()
    {
        var data = Enumerable.Repeat(1f, 1000).ToArray();
        AudioExt.ApproximateGain(data, 1).Should().BeApproximately(1.0, 1e-7);
        AudioExt.ApproximateGain(data, 5).Should().BeApproximately(1.0, 1e-7);
    }

    [Fact]
    public void Both_Agree_ForLengthMultipleOfStride()
    {
        // Generate 1 kHz sine wave at 48 kHz sample rate
        var sampleRate = 48_000;
        var freq = 1_000.0;
        var data = Enumerable.Range(0, 2000)
            .Select(i => (float)Math.Sin(2 * Math.PI * freq * i / sampleRate))
            .ToArray();
        // 2000 is divisible by 5 and 1
        var g = AudioExt.ApproximateGain(data, 5);
        var gA = AudioExt.ApproximateGain(data, 1);
        g.Should().BeApproximately(gA, 1e-3);
    }

    [Fact]
    public void LengthLessThanStride_ApproximateGainIsFinite_ButApproximateGain1IsInfinity()
    {
        var data = new float[] { 0.2f, -0.3f, 0.1f };
        var g = AudioExt.ApproximateGain(data, 5);
        g.Should().BeGreaterThan(0).And.BeLessThan(1);
    }

    [Fact]
    public void SineWave_Stride1_CloseToRms()
    {
        int n = 8192;
        double amp = 0.5;
        var data = new float[n];
        for (int i = 0; i < n; i++)
            data[i] = (float)(amp * Math.Sin(2 * Math.PI * i / 257)); // non-integer periods, decorrelates
        var expectedRms = amp / Math.Sqrt(2);
        var g = AudioExt.ApproximateGain(data, 1);
        g.Should().BeApproximately(expectedRms, 5e-3);
    }
}
