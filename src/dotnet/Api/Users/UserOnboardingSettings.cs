using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// Tracks user progress through onboarding steps.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record UserOnboardingSettings : StoredSettings, IHasOrigin
{
    public const string KvasKey = nameof(UserOnboardingSettings);

    [DataMember, MemoryPackOrder(3)] public string Origin { get; init; } = "";
    [DataMember, MemoryPackOrder(1)] public bool IsAvatarStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(4)] public bool IsCreateChatsStepCompleted { get; init; } // Disabled
    [DataMember, MemoryPackOrder(6)] public bool IsVerifyPhoneStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(8)] public bool IsVerifyEmailStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(9)] public bool IsTimeZoneStepCompleted { get; init; } // Disabled
    [DataMember, MemoryPackOrder(10)] public bool IsDataCollectionStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(16)] public bool IsLanguagesStepCompleted { get; init; }
    // Tutorial steps
    [DataMember, MemoryPackOrder(12)] public bool IsTranscriptionTutorialStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(13)] public bool IsTranscriptReplayTutorialStepCompleted { get; init; } // Disabled
    [DataMember, MemoryPackOrder(14)] public bool IsPlacesTutorialStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(17)] public bool IsSummarizationTutorialStepCompleted { get; init; }

    public bool HasUncompletedSteps()
    {
        var areAllFeatureIndependentStepsCompleted = this is {
            IsAvatarStepCompleted: true,
            IsVerifyPhoneStepCompleted: true,
            // IsCreateChatsStepCompleted: true, // Disabled
            IsVerifyEmailStepCompleted: true,
            // IsTimeZoneStepCompleted: true, // Disabled
            IsDataCollectionStepCompleted: true,
            IsLanguagesStepCompleted: true,
            // Tutorial steps
            // IsTranscriptReplayTutorialStepCompleted: true, // Disabled
            IsTranscriptionTutorialStepCompleted: true,
            IsPlacesTutorialStepCompleted: true,
            IsSummarizationTutorialStepCompleted: true,
        };
        return !areAllFeatureIndependentStepsCompleted;
    }
}
