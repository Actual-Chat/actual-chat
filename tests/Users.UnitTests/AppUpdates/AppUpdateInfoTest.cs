namespace ActualChat.Users.UnitTests.AppUpdates;

public sealed class AppUpdateInfoTest
{
    // ReSharper disable once RedundantAssignment
    [Fact]
    public void EqualityMustNotDependOnWhetherVersionWasRead()
    {
        // arrange
        var now = Moment.EpochStart;
        var a = new AppUpdateInfo(AppKind.Windows, "2.20.130", "2.20.130", now, now);
        var b = new AppUpdateInfo(AppKind.Windows, "2.20.130", "2.20.130", now, now);

        // act
        a.Equals(b).Should().BeTrue("identical instances must be equal to begin with");
        _ = a.Version; // Populates the lazy backing field on a, but not on b

        // assert - GetLatestUpdateInfo's ConsolidationDelay = 0 rests on this: it drops an
        // invalidation only when the recomputed value equals the old one, and the old one has
        // had Version read while the one just deserialized from Redis hasn't
        a.Equals(b).Should().BeTrue("reading a lazily cached property must not change equality");
    }

    [Fact]
    public void CloningMustNotCarryAStaleDerivedValue()
    {
        // arrange
        var now = Moment.EpochStart;
        var original = new AppStoreProbeResult(new Version(2, 20, 109), now);

        // act - the copy constructor copies every field; it doesn't re-run initializers
        var clone = original with { Version = new Version(2, 20, 130) };

        // assert
        clone.VersionString.Should().Be("2.20.130", "a derived member must follow what it derives from");
    }
}
