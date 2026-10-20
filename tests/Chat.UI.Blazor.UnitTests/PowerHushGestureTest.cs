using ActualChat.UI.Blazor.App.Services.Gestures;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class PowerHushGestureTest
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(799, true)]
    [InlineData(800, false)]
    [InlineData(1000, false)]
    [InlineData(3000, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void LockedScreenSequenceShouldRequireLessThan800Milliseconds(int delay, bool expected)
    {
        // arrange
        var gesture = new PowerHushGesture();
        gesture.OnScreenOn(1000, isLocked: true);

        // act
        var isHush = gesture.OnScreenOff(1000 + delay);

        // assert
        isHush.Should().Be(expected);
    }

    [Fact]
    public void UnlockShouldCancelTheSequenceBeforeAnImmediateScreenOff()
    {
        // arrange
        var gesture = new PowerHushGesture();
        gesture.OnScreenOn(1000, isLocked: true);

        // act
        gesture.OnUnlocked();
        var isHush = gesture.OnScreenOff(1100);

        // assert
        isHush.Should().BeFalse();
    }

    [Fact]
    public void WakingAnUnlockedDeviceShouldNotArmHush()
    {
        // arrange
        var gesture = new PowerHushGesture();

        // act
        gesture.OnScreenOn(1000, isLocked: false);
        var isHush = gesture.OnScreenOff(1100);

        // assert
        isHush.Should().BeFalse();
    }

    [Fact]
    public void ScreenOffWithoutScreenOnShouldNotHush()
    {
        // arrange
        var gesture = new PowerHushGesture();

        // act
        var isHush = gesture.OnScreenOff(100);

        // assert
        isHush.Should().BeFalse();
    }

    [Fact]
    public void ScreenOffShouldConsumeTheSequence()
    {
        // arrange
        var gesture = new PowerHushGesture();
        gesture.OnScreenOn(1000, isLocked: true);
        gesture.OnScreenOff(1100).Should().BeTrue();

        // act
        var isHush = gesture.OnScreenOff(1200);

        // assert
        isHush.Should().BeFalse();
    }

    [Fact]
    public void UnlockShouldAllowAFreshLockedSequenceLater()
    {
        // arrange
        var gesture = new PowerHushGesture();
        gesture.OnScreenOn(1000, isLocked: true);
        gesture.OnUnlocked();

        // act
        gesture.OnScreenOn(2000, isLocked: true);
        var isHush = gesture.OnScreenOff(2100);

        // assert
        isHush.Should().BeTrue();
    }
}
