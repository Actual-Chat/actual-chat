namespace ActualChat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record LocalOnboardingSettings : StoredSettings
{
    public const string KvasKey = nameof(LocalOnboardingSettings);

    [DataMember, MemoryPackOrder(0)] public bool IsPermissionsStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(1)] public bool AreCookiesAccepted { get; init; }

    public bool HasUncompletedSteps()
    {
        var areAllFeatureIndependentStepsCompleted = this is {
            IsPermissionsStepCompleted: true,
        };
        return !areAllFeatureIndependentStepsCompleted;
    }
}
