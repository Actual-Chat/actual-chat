namespace ActualChat.Users;

[DataContract]
public sealed record UserOnboardingSettings
{
    public const string KvasKey = nameof(UserOnboardingSettings);

    [DataMember] public bool IsPhoneStepCompleted { get; init; }
    [DataMember] public bool IsAvatarStepCompleted { get; init; }

    public bool ShouldBeShown() {
        if (!IsPhoneStepCompleted) return true;
        if (!IsAvatarStepCompleted) return true;

        return false;
    }
}
