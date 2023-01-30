namespace ActualChat.Core.UnitTests.Mathematics;

public class TrimmedTest
{
    [Theory]
    [InlineData(100, 99, "99+")]
    [InlineData(100, 1000, "100")]
    [InlineData(1000, 1000, "1K+")]
    [InlineData(1001, 1000, "1K+")]
    public void ShouldFormatWithThousandAlias(int value, int limit, string expected)
        => new Trimmed<int>(value, limit).FormatK().Should().Be(expected);
}
