namespace ActualChat.Mui.UnitTests;

public class MuiPeriodsTest
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DaySettingsGiveRollingDayWindowsShiftedByWeeks()
    {
        // arrange
        var settings = new MuiPeriodSettings(MuiInterval.Day, MuiStep.Week, 2);

        // act
        var windows = settings.GetWindows(Now);

        // assert
        windows.Should().HaveCount(3);
        windows[0].To.Should().Be(Now);
        windows[0].From.Should().Be(Now.AddDays(-1));
        windows[1].To.Should().Be(Now.AddDays(-7));
        windows[2].To.Should().Be(Now.AddDays(-14));
        windows.Select(w => w.To - w.From).Should().AllBeEquivalentTo(TimeSpan.FromDays(1));
        windows.Select(w => w.Label).Should().Equal("Last 24 hours", "1 week earlier", "2 weeks earlier");
    }

    [Fact]
    public void WeekSettingsGiveWeekWindowsShiftedByMonthsAndQuarters()
    {
        // arrange
        var months = new MuiPeriodSettings(MuiInterval.Week, MuiStep.Month, 3);
        var quarters = new MuiPeriodSettings(MuiInterval.Week, MuiStep.Quarter, 1);

        // act
        var monthWindows = months.GetWindows(Now);
        var quarterWindows = quarters.GetWindows(Now);

        // assert
        monthWindows[0].Label.Should().Be("Last 7 days");
        monthWindows[3].To.Should().Be(Now.AddMonths(-3));
        monthWindows[3].Label.Should().Be("3 months earlier");
        quarterWindows[1].To.Should().Be(Now.AddMonths(-3));
        quarterWindows[1].Label.Should().Be("3 months earlier");
    }

    [Fact]
    public void IntervalStepPlacesWindowsBackToBack()
    {
        // arrange
        var settings = new MuiPeriodSettings(MuiInterval.Day, MuiStep.Interval, 2);

        // act
        var windows = settings.GetWindows(Now);

        // assert
        windows[1].To.Should().Be(windows[0].From);
        windows[2].To.Should().Be(windows[1].From);
    }

    [Fact]
    public void CountIsClamped()
    {
        // arrange
        var settings = new MuiPeriodSettings(Count: 100);

        // act
        var windows = settings.GetWindows(Now);

        // assert
        windows.Should().HaveCount(MuiPeriodSettings.MaxCount + 1);
    }

    [Fact]
    public void StorageStringRoundTrips()
    {
        // arrange
        var settings = new MuiPeriodSettings(MuiInterval.Week, MuiStep.Quarter, 7);

        // act
        var restored = MuiPeriodSettings.FromStorageString(settings.ToStorageString());

        // assert
        restored.Should().Be(settings);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("Day|Nope|1")]
    public void BadStorageStringGivesDefaults(string? text)
        => MuiPeriodSettings.FromStorageString(text).Should().Be(new MuiPeriodSettings());

    [Fact]
    public void MetricSqlSelectsEveryMetricWithWindowLiterals()
    {
        // arrange
        var metrics = new[] {
            new MuiMetric("a", "A", "select count(*) from t where x >= {from} and x < {to}"),
            new MuiMetric("b", "B", "select 2"),
        };
        var window = new MuiWindow(0, Now.AddDays(-1), Now, "w");

        // act
        var sql = MuiMetricSql.Build(metrics, window);

        // assert
        sql.Should().Contain("timestamptz '2026-10-08T12:00:00.000Z'");
        sql.Should().Contain("timestamptz '2026-10-09T12:00:00.000Z'");
        sql.Should().Contain("as \"a\"");
        sql.Should().Contain("(select 2) as \"b\"");
        sql.Should().NotContain("{from}");
    }

    [Fact]
    public void MetricWithMaxAgeIsAvailableOnlyForRecentWindows()
    {
        // arrange
        var metric = new MuiMetric("a", "A", "select 1", TimeSpan.FromDays(1));
        var unlimited = new MuiMetric("b", "B", "select 1");
        var windows = new MuiPeriodSettings(MuiInterval.Day, MuiStep.Week, 1).GetWindows(Now);

        // act & assert
        metric.IsAvailable(windows[0], Now).Should().BeTrue();
        metric.IsAvailable(windows[1], Now).Should().BeFalse();
        unlimited.IsAvailable(windows[1], Now).Should().BeTrue();
    }

    [Fact]
    public void SinglePointQueryIsRunnableWithoutPlaceholders()
    {
        // arrange
        var metric = new MuiMetric("a", "A", "select count(*) from t where x >= {from} and x < {to}");
        var window = new MuiWindow(1, Now.AddDays(-8), Now.AddDays(-7), "w");

        // act
        var sql = MuiMetricSql.BuildOne(metric, window);

        // assert
        sql.Should().Be("select count(*) from t where x >= timestamptz '2026-10-01T12:00:00.000Z'"
            + " and x < timestamptz '2026-10-02T12:00:00.000Z'");
    }

    [Theory]
    [InlineData(120, 100, 20)]
    [InlineData(50, 100, -50)]
    public void ChangePercentIsRelativeToComparedValue(double current, double compared, double expected)
        => MuiMetricSql.GetChangePercent(current, compared).Should().Be(expected);

    [Fact]
    public void ChangePercentIsUnknownForZeroBase()
        => MuiMetricSql.GetChangePercent(5, 0).Should().BeNull();
}
