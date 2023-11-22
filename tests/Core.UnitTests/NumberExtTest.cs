namespace ActualChat.Core.UnitTests;

public class NumberExtTest
{
    [Fact]
    public void ParseIntTest()
    {
        NumberExt.ParsePositiveInt("0").Should().Be(0);
        NumberExt.ParsePositiveInt("1").Should().Be(1);
        NumberExt.ParsePositiveInt("10").Should().Be(10);
        NumberExt.ParsePositiveInt("109").Should().Be(109);
        NumberExt.ParsePositiveInt("2147483647").Should().Be(int.MaxValue);
        NumberExt.TryParsePositiveInt("", out _).Should().BeFalse();
        NumberExt.TryParsePositiveInt("-", out _).Should().BeFalse();
        NumberExt.TryParsePositiveInt("-1", out _).Should().BeFalse();
        NumberExt.TryParsePositiveInt("2147483648", out _).Should().BeFalse();
        NumberExt.TryParsePositiveInt("2147483656", out _).Should().BeFalse();
        NumberExt.TryParsePositiveInt("2147483657", out _).Should().BeFalse();
        NumberExt.TryParsePositiveInt("2147483658", out _).Should().BeFalse();

        NumberExt.ParseInt("0").Should().Be(0);
        NumberExt.ParseInt("1").Should().Be(1);
        NumberExt.ParseInt("-1").Should().Be(-1);
        NumberExt.ParseInt("-103").Should().Be(-103);
        NumberExt.ParseInt((-int.MaxValue).Format()).Should().Be(-int.MaxValue);
    }

    [Fact]
    public void ParseLongTest()
    {
        NumberExt.ParsePositiveLong("0").Should().Be(0);
        NumberExt.ParsePositiveLong("1").Should().Be(1);
        NumberExt.ParsePositiveLong("10").Should().Be(10);
        NumberExt.ParsePositiveLong("109").Should().Be(109);
        NumberExt.ParsePositiveLong("9223372036854775807").Should().Be(long.MaxValue);
        NumberExt.TryParsePositiveLong("", out _).Should().BeFalse();
        NumberExt.TryParsePositiveLong("-", out _).Should().BeFalse();
        NumberExt.TryParsePositiveLong("-1", out _).Should().BeFalse();
        NumberExt.TryParsePositiveLong("9223372036854775808", out _).Should().BeFalse();
        NumberExt.TryParsePositiveLong("9223372036854775816", out _).Should().BeFalse();
        NumberExt.TryParsePositiveLong("9223372036854775817", out _).Should().BeFalse();
        NumberExt.TryParsePositiveLong("9223372036854775818", out _).Should().BeFalse();

        NumberExt.ParseLong("0").Should().Be(0);
        NumberExt.ParseLong("1").Should().Be(1);
        NumberExt.ParseLong("-1").Should().Be(-1);
        NumberExt.ParseLong("-103").Should().Be(-103);
        NumberExt.ParseLong((-long.MaxValue).Format()).Should().Be(-long.MaxValue);
    }
}
