using ActualChat.Users.Flows;
using TimeZoneConverter;

namespace ActualChat.Users.UnitTests.Flows;

public class DigestFlowTest
{
    private static readonly TimeZoneInfo NewYork = TZConvert.GetTimeZoneInfo("America/New_York");
    private static readonly TimeSpan NineAm = TimeSpan.FromHours(9);

    [Fact]
    public void FirstRunShouldBeDue()
        => DigestFlow.IsDue(NewYork, NineAm, default, At(2026, 9, 24, 3, 0)).Should().BeTrue();

    [Theory]
    [InlineData(10, 0)]
    [InlineData(23, 59)]
    public void RunLaterTheSameDayShouldNotBeDue(int hour, int minute)
        => DigestFlow.IsDue(NewYork, NineAm, At(2026, 9, 24, 9, 0), At(2026, 9, 24, hour, minute))
            .Should().BeFalse("the digest of this day was already taken at 09:00");

    [Fact]
    public void RunBeforeNextDigestTimeShouldNotBeDue()
        => DigestFlow.IsDue(NewYork, NineAm, At(2026, 9, 24, 9, 0), At(2026, 9, 25, 8, 59))
            .Should().BeFalse();

    [Fact]
    public void RunAtNextDigestTimeShouldBeDue()
        => DigestFlow.IsDue(NewYork, NineAm, At(2026, 9, 24, 9, 0), At(2026, 9, 25, 9, 0))
            .Should().BeTrue();

    [Fact]
    public void EarlyRunShouldNotConsumeTheDigest()
    {
        // arrange
        var lastSentAt = At(2026, 9, 24, 9, 0);
        var earlyRunAt = At(2026, 9, 25, 3, 0);

        // act
        var isEarlyRunDue = DigestFlow.IsDue(NewYork, NineAm, lastSentAt, earlyRunAt);
        var isDigestTimeDue = DigestFlow.IsDue(NewYork, NineAm, earlyRunAt, At(2026, 9, 25, 9, 0));

        // assert
        isEarlyRunDue.Should().BeFalse();
        isDigestTimeDue.Should().BeTrue("a skipped early run must not push the digest to the next day");
    }

    [Fact]
    public void LaterDigestTimeShouldBeDueTheSameDay()
        => DigestFlow.IsDue(NewYork, TimeSpan.FromHours(18), At(2026, 9, 24, 9, 0), At(2026, 9, 24, 18, 0))
            .Should().BeTrue("moving the digest time later makes it due again once the new time passes");

    private static Moment At(int year, int month, int day, int hour, int minute)
        => new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified).ToMoment(NewYork);
}
