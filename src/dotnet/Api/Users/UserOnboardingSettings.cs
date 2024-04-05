using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record UserOnboardingSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserOnboardingSettings);

    [Obsolete("Use IsVerifyPhoneStepCompleted.")]
    [DataMember, MemoryPackOrder(0)] public bool IsPhoneStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(1)] public bool IsAvatarStepCompleted { get; init; }
    [Obsolete("Must not be used.")]
    [DataMember, MemoryPackOrder(2)] public Moment LastShownAt { get; init; }
    [DataMember, MemoryPackOrder(3)] public string Origin { get; init; } = "";
    [DataMember, MemoryPackOrder(4)] public bool IsCreateChatsStepCompleted { get; init; }
    [Obsolete("Use IsVerifyEmailStepCompleted")]
    [DataMember, MemoryPackOrder(5)] public bool IsEmailStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(6)] public bool IsVerifyPhoneStepCompleted { get; init; }
    [Obsolete("Use LocalOnboardingSettings.IsPermissionsStepCompleted.")]
    [DataMember, MemoryPackOrder(7)] public bool IsPermissionsStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(8)] public bool IsVerifyEmailStepCompleted { get; init; }
    [DataMember, MemoryPackOrder(9)] public bool IsTimeZoneStepCompleted { get; init; }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool HasUncompletedSteps
        => this is not {
            IsAvatarStepCompleted: true,
            IsVerifyPhoneStepCompleted: true,
            IsCreateChatsStepCompleted: true,
            IsVerifyEmailStepCompleted: true,
            IsTimeZoneStepCompleted: true,
        };
}
