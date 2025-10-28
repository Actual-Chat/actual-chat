namespace ActualChat.Core.UnitTests;

public class MomentExtTest
{
    [Theory]
    [InlineData("2024-02-29T07:31:19.1651302Z", "2024-02-29T07:31:20.0Z")]
    [InlineData("2024-02-29T07:33:23.0000000Z", "2024-02-29T07:33:23.0Z")]
    [InlineData("2024-02-29T07:33:23.1234567Z", "2024-02-29T07:33:24.0Z")]
    [InlineData("2024-02-29T23:59:59.5000000Z", "2024-03-01T00:00:00.0Z")]
    [InlineData("2024-03-01T00:00:00.0000000Z", "2024-03-01T00:00:00.0Z")]
    public void CeilingTest(string sTime, string sExpected)
        => Moment.Parse(sTime).Ceiling(TimeSpan.FromSeconds(1)).Should().Be(Moment.Parse(sExpected));
}
