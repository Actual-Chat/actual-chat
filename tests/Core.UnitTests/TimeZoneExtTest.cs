using System.Globalization;
using TimeZoneConverter;

namespace ActualChat.Core.UnitTests;

public class TimeZoneExtTest
{
    [Theory]
    [InlineData("2024-01-01T07:00:00Z", "09:00", "UTC", "02:00")]
    [InlineData("2024-01-01T07:00:00Z", "09:00", "America/New_York", "07:00")]
    [InlineData("2024-01-01T07:00:00Z", "09:00", "Europe/Moscow", "23:00")]
    public void ShouldCalculateDelay(string sNow, string sTime, string sTimeZone, string sExpected)
    {
        // arrange
        var now = DateTimeOffset.Parse(sNow, DateTimeFormatInfo.InvariantInfo).ToMoment();
        var timeOfDay = TimeSpan.Parse(sTime, DateTimeFormatInfo.InvariantInfo);
        var timeZone = TZConvert.GetTimeZoneInfo(sTimeZone);
        var expected = TimeSpan.Parse(sExpected, DateTimeFormatInfo.InvariantInfo);

        // act
        var delay = timeZone.NextTimeOfDay(timeOfDay, now) - now;

        // assert
        delay.Should().Be(expected);
    }
}
