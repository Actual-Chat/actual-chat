namespace ActualChat.Mui.UnitTests;

public class MuiSettingsTest
{
    [Fact]
    public void SettingsWithChartRoundTrip()
    {
        // arrange
        var settings = new MuiPeriodSettings(MuiInterval.Day, MuiStep.Month, 3, MuiChartKind.Line);

        // act
        var restored = MuiPeriodSettings.FromStorageString(settings.ToStorageString());

        // assert
        restored.Should().Be(settings);
    }

    [Fact]
    public void OldStorageStringsStillLoad()
        => MuiPeriodSettings.FromStorageString("Week|Month|3")
            .Should().Be(new MuiPeriodSettings(MuiInterval.Week, MuiStep.Month, 3));

    [Fact]
    public void WindowsHaveShortLabels()
    {
        // arrange
        var settings = new MuiPeriodSettings(MuiInterval.Day, MuiStep.Month, 2);

        // act
        var labels = settings.GetWindows(new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc))
            .Select(w => w.ShortLabel);

        // assert
        labels.Should().Equal("now", "-1mo", "-2mo");
    }
}
