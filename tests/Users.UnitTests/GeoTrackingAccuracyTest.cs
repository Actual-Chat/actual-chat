namespace ActualChat.Users.UnitTests;

public class GeoTrackingAccuracyTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void NextCyclesThroughAllLevelsAndWraps()
    {
        GeoTrackingAccuracy.High.Next().Should().Be(GeoTrackingAccuracy.Balanced);
        GeoTrackingAccuracy.Balanced.Next().Should().Be(GeoTrackingAccuracy.Low);
        GeoTrackingAccuracy.Low.Next().Should().Be(GeoTrackingAccuracy.High);
    }
}
