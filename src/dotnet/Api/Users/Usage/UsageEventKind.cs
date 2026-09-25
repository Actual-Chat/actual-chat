namespace ActualChat.Users;

public enum UsageEventKind
{
    Speech = 0,
    Message = 1,
    LiveSession = 2,
    Contact = 3,
    ActiveDay = 4,
    SignUp = 5,
    OnboardingStep = 6,
}

public static class UsageEventKindExt
{
    // A UsageDay row marks its day as active, so only activity kinds may create one
    public static bool IsDayRollup(this UsageEventKind kind)
        => kind is not (UsageEventKind.SignUp or UsageEventKind.OnboardingStep);
}
