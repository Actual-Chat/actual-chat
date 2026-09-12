namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record LocalOnboardingSettings : StoredSettings
{
    public const string KvasKey = nameof(LocalOnboardingSettings);

    [DataMember, MemoryPackOrder(0), Key(0)] public bool IsPermissionsStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public bool AreCookiesAccepted { get; init; }
    [DataMember, MemoryPackOrder(2), Key(2)] public int PasskeyNudgeCount { get; init; }
    [DataMember, MemoryPackOrder(3), Key(3)] public Moment PasskeyNudgeLastAt { get; init; }

    public bool HasUncompletedSteps()
    {
        var areAllFeatureIndependentStepsCompleted = this is {
            IsPermissionsStepCompleted: true,
        };
        return !areAllFeatureIndependentStepsCompleted;
    }
}
