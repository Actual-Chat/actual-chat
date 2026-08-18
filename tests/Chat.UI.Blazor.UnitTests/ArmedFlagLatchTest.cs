using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public sealed class ArmedFlagLatchTest
{
    [Fact]
    public void StartupEmptyIsNotPersisted()
    {
        // act
        var mustPersist = ActivitiesBackend.ShouldPersistArmed(false, hasEverBeenArmed: false);

        // assert
        mustPersist.Should().BeFalse();
    }

    [Fact]
    public void ArmedIsAlwaysPersisted()
    {
        // act
        var fromStartup = ActivitiesBackend.ShouldPersistArmed(true, hasEverBeenArmed: false);
        var fromSteadyState = ActivitiesBackend.ShouldPersistArmed(true, hasEverBeenArmed: true);

        // assert
        fromStartup.Should().BeTrue();
        fromSteadyState.Should().BeTrue();
    }

    [Fact]
    public void AStoredArmedFlagSeedsTheLatch()
    {
        // act: first recompute of a session that launched armed, but resolves to no armed chats -
        // which is what a disarm on another device looks like from here.
        var hasEverBeenArmed = ActivitiesBackend.NextHasEverBeenArmed(null, isArmedPersisted: true, isArmed: false);

        // assert: the disarm must be writable, or the app stays armed for good
        hasEverBeenArmed.Should().BeTrue();
        ActivitiesBackend.ShouldPersistArmed(false, hasEverBeenArmed).Should().BeTrue();
    }

    [Fact]
    public void AnUnarmedStartStillSwallowsTheFirstEmptySet()
    {
        // act
        var hasEverBeenArmed = ActivitiesBackend.NextHasEverBeenArmed(null, isArmedPersisted: false, isArmed: false);

        // assert
        hasEverBeenArmed.Should().BeFalse();
        ActivitiesBackend.ShouldPersistArmed(false, hasEverBeenArmed).Should().BeFalse();
    }

    [Fact]
    public void TheLatchSticksOnceArmedIsSeen()
    {
        // act
        var afterArmed = ActivitiesBackend.NextHasEverBeenArmed(false, isArmedPersisted: false, isArmed: true);
        var afterEmpty = ActivitiesBackend.NextHasEverBeenArmed(afterArmed, isArmedPersisted: false, isArmed: false);

        // assert
        afterArmed.Should().BeTrue();
        afterEmpty.Should().BeTrue();
    }

    [Fact]
    public void DisarmAfterArmedIsPersisted()
    {
        // act
        var mustPersist = ActivitiesBackend.ShouldPersistArmed(false, hasEverBeenArmed: true);

        // assert
        mustPersist.Should().BeTrue();
    }
}
