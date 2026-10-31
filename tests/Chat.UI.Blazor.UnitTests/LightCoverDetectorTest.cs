using ActualChat.UI.Blazor.App.Services.Gestures;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class LightCoverDetectorTest
{
    private static readonly Moment T0 = Moment.EpochStart + TimeSpan.FromDays(20_000);

    [Fact]
    public void PocketDarknessCovers()
    {
        // arrange: a pocket reads 0-1 lux on the SM-S948U1
        var detector = new LightCoverDetector();

        // act
        detector.Process(8f, T0);
        var isCoveredAtOnce = detector.Process(0f, At(1));
        var isCoveredLater = detector.Tick(At(2));

        // assert
        isCoveredAtOnce.Should().BeFalse("a dark reading must hold before it counts");
        isCoveredLater.Should().BeTrue();
    }

    [Fact]
    public void BriefShadowInDimRoomDoesNotCover()
    {
        // arrange: a hand over the sensor in a 1-3 lux room reads 0 for a moment
        var detector = new LightCoverDetector();

        // act
        detector.Process(2f, T0);
        detector.Process(0f, At(1));
        var isCovered = detector.Process(2f, At(1.4));
        detector.Tick(At(3));

        // assert
        isCovered.Should().BeFalse();
        detector.IsCovered.Should().BeFalse();
    }

    [Fact]
    public void AlreadyDarkAtStartCovers()
    {
        // arrange: sensing can start with the phone already pocketed
        var detector = new LightCoverDetector();

        // act
        var isCovered = detector.Process(0f, T0);

        // assert
        isCovered.Should().BeTrue();
    }

    [Fact]
    public void FirstReadingSplitsAtOneLux()
    {
        // arrange
        var below = new LightCoverDetector();
        var at = new LightCoverDetector();

        // act
        var isBelowCovered = below.Process(0.9f, T0);
        var isAtCovered = at.Process(1f, T0);

        // assert
        isBelowCovered.Should().BeTrue();
        isAtCovered.Should().BeFalse();
    }

    [Fact]
    public void DimLightNeverCovers()
    {
        // arrange
        var detector = new LightCoverDetector();

        // act
        var isCovered = detector.Process(1f, T0);

        // assert
        isCovered.Should().BeFalse();
    }

    [Fact]
    public void ShortFlickerKeepsCover()
    {
        // arrange: moving in a pocket lets in up to 5 lux for ~0.5s
        var detector = new LightCoverDetector();
        detector.Process(0f, T0);

        // act
        detector.Process(5f, At(10));
        var duringFlicker = detector.Process(3f, At(10.5));
        var afterFlicker = detector.Process(0f, At(10.7));

        // assert
        duringFlicker.Should().BeTrue();
        afterFlicker.Should().BeTrue();
    }

    [Fact]
    public void SustainedLightUncovers()
    {
        // arrange
        var detector = new LightCoverDetector();
        detector.Process(0f, T0);

        // act
        var whenLit = detector.Process(6f, At(10));
        var afterDelay = detector.Process(7f, At(11));

        // assert
        whenLit.Should().BeTrue();
        afterDelay.Should().BeFalse();
    }

    [Fact]
    public void BetweenThresholdsKeepsState()
    {
        // arrange
        var detector = new LightCoverDetector();
        detector.Process(0f, T0);

        // act: 0.5-1.5 lux neither covers nor uncovers
        var isCovered = detector.Process(1f, At(60));

        // assert
        isCovered.Should().BeTrue();
    }

    [Fact]
    public void ResetForgetsCover()
    {
        // arrange
        var detector = new LightCoverDetector();
        detector.Process(0f, T0);

        // act
        detector.Reset();
        var isCovered = detector.Process(1f, At(1));

        // assert
        isCovered.Should().BeFalse();
    }

    // Private methods

    private static Moment At(double seconds)
        => T0 + TimeSpan.FromSeconds(seconds);
}
