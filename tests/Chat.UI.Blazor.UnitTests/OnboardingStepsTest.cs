using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.Components;

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

    [Fact]
    public void EveryOnboardingStepShouldHaveAName()
    {
        // arrange - step components that exist but OnboardingModal doesn't render
        var disabledStepTypes = new[] {
            typeof(TranscriptReplayTutorialStep),
            typeof(TimeZoneStep),
            typeof(CreateChatsStep),
        };

        // act
        var unnamedSteps = typeof(OnboardingModal).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IStep).IsAssignableFrom(t))
            .Except(disabledStepTypes)
            .Select(OnboardingSteps.GetName)
            .Where(name => !OnboardingSteps.IsValid(name))
            .ToList();

        // assert
        unnamedSteps.Should().BeEmpty(
            "the server rejects a step missing from OnboardingSteps.All, so it would never be counted; "
            + "add it there and to the report's step order (docs/usage-metrics.md), "
            + "or list it above if the modal doesn't render it");
    }
}
