namespace ActualChat.Mui.UnitTests;

public class MuiLiveTest
{
    [Theory]
    [InlineData(null, "-")]
    [InlineData(0.4, "<1 s")]
    [InlineData(12.9, "12 s")]
    [InlineData(185.0, "3 min 5 s")]
    [InlineData(7500.0, "2 h 5 min")]
    [InlineData(200000.0, "2 d 7 h")]
    public void FormatDurationShouldUseTheTwoLargestUnits(double? seconds, string expected)
        => MuiLive.FormatDuration(seconds).Should().Be(expected);

    [Theory]
    [InlineData(0, "0")]
    [InlineData(12, "12 (0.20/s)")]
    public void FormatRateShouldShowCountAndRatePerSecond(long count, string expected)
        => MuiLive.FormatRate(count, 60).Should().Be(expected);

    [Fact]
    public void ToLongAndToSecondsShouldTolerateNullAndGarbage()
    {
        // act & assert
        MuiLive.ToLong(null).Should().Be(0);
        MuiLive.ToLong("17").Should().Be(17);
        MuiLive.ToSeconds(null).Should().BeNull();
        MuiLive.ToSeconds("abc").Should().BeNull();
        MuiLive.ToSeconds("1.5").Should().Be(1.5);
    }
}
