using ActualChat.Kvas;

namespace ActualChat.Users;

/// <summary>
/// Tracks user progress through onboarding steps.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record UserOnboardingSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserOnboardingSettings);

    [DataMember, MemoryPackOrder(1)] public bool IsAvatarStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(3)] public string Origin { get; init; } = "";
    [DataMember, MemoryPackOrder(4)] public bool IsCreateChatsStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(6)] public bool IsVerifyPhoneStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(8)] public bool IsVerifyEmailStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(9)] public bool IsTimeZoneStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(10)] public bool IsDataCollectionStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(12)] public bool IsSpeechTranscriptionStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(13)] public bool IsTranscriptPlaybackStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(14)] public bool IsPlacesFeatureStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(16)] public bool IsLanguagesStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(17)] public bool IsSummarizationStepCompleted { get; init; }

    public bool HasUncompletedSteps()
    {
        var areAllFeatureIndependentStepsCompleted = this is {
            IsAvatarStepCompleted: true,
            IsVerifyPhoneStepCompleted: true,
            IsCreateChatsStepCompleted: true,
            IsVerifyEmailStepCompleted: true,
            IsTimeZoneStepCompleted: true,
            IsDataCollectionStepCompleted: true,
            IsSpeechTranscriptionStepCompleted: true,
            IsTranscriptPlaybackStepCompleted: true,
            IsPlacesFeatureStepCompleted: true,
            IsSummarizationStepCompleted: true,
            IsLanguagesStepCompleted: true,
        };
        return !areAllFeatureIndependentStepsCompleted;
    }
}
