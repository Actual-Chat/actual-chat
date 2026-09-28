using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.Chat.UI.Blazor.UnitTests;

public class OnboardingStepsTest
{
    [Fact]
    public void OnboardingStepNamesShouldMatchModalSteps()
    {
        // arrange - the steps OnboardingModal renders, in its order; a rename must fail here, not drop data
        var stepTypes = new[] {
            typeof(TranscriptionTutorialStep),
            typeof(PlacesTutorialStep),
            typeof(SummarizationTutorialStep),
            typeof(PhoneStep),
            typeof(EmailStep),
            typeof(AvatarStep),
            typeof(PermissionsStep),
            typeof(LanguagesStep),
            typeof(DataCollectionStep),
            typeof(PasskeyStep),
        };

        // act
        var names = stepTypes.Select(OnboardingSteps.GetName).Append(OnboardingSteps.Finished).ToList();

        // assert
        names.Should().Equal(OnboardingSteps.All);
    }
}
