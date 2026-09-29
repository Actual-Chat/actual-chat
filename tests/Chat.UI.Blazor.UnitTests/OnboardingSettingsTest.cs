using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class OnboardingSettingsTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void WithAllStepsCompletedShouldLeaveNoUncompletedSteps()
    {
        // act
        var user = new UserOnboardingSettings().WithAllStepsCompleted();
        var local = new LocalOnboardingSettings().WithAllStepsCompleted();

        // assert
        user.HasUncompletedSteps().Should().BeFalse();
        local.HasUncompletedSteps().Should().BeFalse();
    }
}
