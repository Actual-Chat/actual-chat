using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
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
    [DataMember, MemoryPackOrder(11)] public bool IsChatRouletteStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(12)] public bool IsSpeechTranscriptionStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(13)] public bool IsTranscriptPlaybackStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(14)] public bool IsPlacesFeatureStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(15)] public bool IsJoinPlaceStepCompleted { get; init; }

    public bool HasUncompletedSteps(bool enableChatRouletteUI)
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
            IsJoinPlaceStepCompleted: true,
        };
        if (!areAllFeatureIndependentStepsCompleted)
            return true;
        if (enableChatRouletteUI && !IsChatRouletteStepCompleted)
            return true;

        return false;
    }
}
