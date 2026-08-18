using ActualChat.UI.Blazor.App.Services.Gestures;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ProximityGuardTest
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);

    [Fact]
    public void ShortSpikeNeverSuppresses()
    {
        // arrange: the OnePlus CPH2747 emits 0.2-1s false "near" readings while the phone is still
        var guard = new ProximityGuard();

        // act
        guard.SetCovered(true, T0);
        var duringSpike = guard.IsSuppressing(Upright(T0 + TimeSpan.FromSeconds(0.2)));
        guard.SetCovered(false, T0 + TimeSpan.FromSeconds(0.36));
        var afterSpike = guard.IsSuppressing(Upright(T0 + TimeSpan.FromSeconds(0.4)));

        // assert
        duringSpike.Should().BeFalse();
        afterSpike.Should().BeFalse();
    }

    [Fact]
    public void SustainedCoverSuppressesWhenUpright()
    {
        // arrange
        var guard = new ProximityGuard();

        // act
        guard.SetCovered(true, T0);
        var isSuppressing = guard.IsSuppressing(Upright(T0 + TimeSpan.FromSeconds(0.6)));

        // assert
        isSuppressing.Should().BeTrue();
    }

    [Fact]
    public void FlatFaceUpVetoesSuppression()
    {
        // arrange
        var guard = new ProximityGuard();

        // act: a phone resting screen-up on a desk isn't pocketed, however long "near" holds
        guard.SetCovered(true, T0);
        var isSuppressing = guard.IsSuppressing(FaceUp(T0 + TimeSpan.FromSeconds(30)));

        // assert
        isSuppressing.Should().BeFalse();
    }

    [Fact]
    public void FaceDownIsStillSuppressed()
    {
        // arrange
        var guard = new ProximityGuard();

        // act
        guard.SetCovered(true, T0);
        var isSuppressing = guard.IsSuppressing(FaceDown(T0 + TimeSpan.FromSeconds(30)));

        // assert
        isSuppressing.Should().BeTrue();
    }

    [Fact]
    public void MotionDoesNotVetoSuppression()
    {
        // arrange: mid-shake the reading isn't gravity-dominated, so it says nothing about carry
        var guard = new ProximityGuard();

        // act
        guard.SetCovered(true, T0);
        var isSuppressing = guard.IsSuppressing(new SensorSample(T0 + TimeSpan.FromSeconds(5), 0.2f, 0.3f, 1.9f));

        // assert
        isSuppressing.Should().BeTrue();
    }

    [Fact]
    public void UncoverClearsSuppression()
    {
        // arrange
        var guard = new ProximityGuard();

        // act
        guard.SetCovered(true, T0);
        var whileCovered = guard.IsSuppressing(Upright(T0 + TimeSpan.FromSeconds(0.6)));
        guard.SetCovered(false, T0 + TimeSpan.FromSeconds(1));
        var afterUncover = guard.IsSuppressing(Upright(T0 + TimeSpan.FromSeconds(1.1)));

        // assert
        whileCovered.Should().BeTrue();
        afterUncover.Should().BeFalse();
    }

    // Private methods

    private static SensorSample FaceUp(Moment at) => new(at, -0.01f, 0.02f, 0.99f);
    private static SensorSample FaceDown(Moment at) => new(at, -0.01f, 0.02f, -0.99f);
    private static SensorSample Upright(Moment at) => new(at, 0.02f, 0.99f, -0.01f);
}
