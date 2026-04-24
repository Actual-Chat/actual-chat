using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// Tracks user progress through onboarding steps.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public sealed partial record UserOnboardingSettings : StoredSettings, IHasOrigin
{
    public const string KvasKey = nameof(UserOnboardingSettings);

    [DataMember, MemoryPackOrder(3), Key(1)] public string Origin { get; init; } = "";
    [DataMember, MemoryPackOrder(1), Key(0)] public bool IsAvatarStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(4), Key(2)] public bool IsCreateChatsStepCompleted { get; init; } // Disabled
    [DataMember, MemoryPackOrder(6), Key(3)] public bool IsVerifyPhoneStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(8), Key(4)] public bool IsVerifyEmailStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(9), Key(5)] public bool IsTimeZoneStepCompleted { get; init; } // Disabled
    [DataMember, MemoryPackOrder(10), Key(6)] public bool IsDataCollectionStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(16), Key(10)] public bool IsLanguagesStepCompleted { get; init; }
    // Tutorial steps
    [DataMember, MemoryPackOrder(12), Key(7)] public bool IsTranscriptionTutorialStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(13), Key(8)] public bool IsTranscriptReplayTutorialStepCompleted { get; init; } // Disabled
    [DataMember, MemoryPackOrder(14), Key(9)] public bool IsPlacesTutorialStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(17), Key(11)] public bool IsSummarizationTutorialStepCompleted { get; init; }

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
