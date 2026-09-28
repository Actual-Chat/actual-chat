namespace ActualChat.Users;

/// <summary>
/// The <c>SourceId</c>s of <see cref="UsageEventKind.OnboardingStep"/> events, in the order the onboarding
/// modal shows them; <see cref="Finished"/> marks a completed onboarding.
/// </summary>
public static class OnboardingSteps
{
    public const string Finished = "Finished";

    public static readonly IReadOnlyList<string> All = [
        "TranscriptionTutorial",
        "PlacesTutorial",
        "SummarizationTutorial",
        "Phone",
        "Email",
        "Avatar",
        "Permissions",
        "Languages",
        "DataCollection",
        "Passkey",
        Finished,
    ];

    public static bool IsValid(string step)
        => All.Contains(step);

    public static string GetName(Type stepType)
    {
        var name = stepType.Name;
        return name.EndsWith("Step") ? name[..^4] : name;
    }
}
