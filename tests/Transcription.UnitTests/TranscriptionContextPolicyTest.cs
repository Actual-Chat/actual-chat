namespace ActualChat.Transcription.UnitTests;

public sealed class TranscriptionContextPolicyTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly TranscriptionContextPolicy Policy = new() {
        MinChars = 100,
        MaxChars = 800,
        CharsPerAudioSecond = 20,
    };

    [Fact]
    public void StreamingGetsTheFlatAllowance()
        => Policy.GetMaxChars(null).Should().Be(800);

    [Theory]
    [InlineData(0, 100)]
    [InlineData(5, 100)]
    [InlineData(20, 400)]
    [InlineData(40, 800)]
    [InlineData(600, 800)]
    public void BudgetScalesWithDurationBetweenTheBounds(double seconds, int expected)
        => Policy.GetMaxChars(TimeSpan.FromSeconds(seconds)).Should().Be(expected);

    [Fact]
    public void ZeroRatioOptsOutOfScaling()
    {
        // arrange
        var policy = Policy with { CharsPerAudioSecond = 0 };

        // act
        var maxChars = policy.GetMaxChars(TimeSpan.FromSeconds(1));

        // assert
        maxChars.Should().Be(800);
    }
}
