namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record LocalOnboardingSettings : StoredSettings
{
    public const string KvasKey = nameof(LocalOnboardingSettings);

    [DataMember, MemoryPackOrder(0), Key(0)] public bool IsPermissionsStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public bool AreCookiesAccepted { get; init; }
    // Keys 2 and 3 held the passkey nudge snooze, which moved to UserOnboardingSettings and LocalStorage

    public bool HasUncompletedSteps()
    {
        var areAllFeatureIndependentStepsCompleted = this is {
            IsPermissionsStepCompleted: true,
        };
        return !areAllFeatureIndependentStepsCompleted;
    }
}
