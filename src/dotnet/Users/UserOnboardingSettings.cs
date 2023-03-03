using ActualChat.Kvas;

namespace ActualChat.Users;

[DataContract]
public sealed record UserOnboardingSettings : IHasOrigin
{
    public const string KvasKey = nameof(UserOnboardingSettings);

    [DataMember] public bool IsPhoneStepCompleted { get; init; }
    [DataMember] public bool IsAvatarStepCompleted { get; init; }
    [DataMember] public Moment LastShownAt { get; init; }
    [DataMember] public string Origin { get; init; } = "";

    [JsonIgnore, Newtonsoft.Json.JsonIgnore]
    public bool HasUncompletedSteps
        => this is not {
            IsAvatarStepCompleted: true,
            IsPhoneStepCompleted: true,
        };
}
