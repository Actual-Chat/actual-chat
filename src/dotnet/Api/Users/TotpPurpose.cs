namespace ActualChat.Users;

/// <summary>
/// Specifies the purpose of a time-based one-time password (TOTP).
/// </summary>
public enum TotpPurpose
{
    SignInPhone,
    SignInEmail,
    VerifyPhone,
    VerifyEmail,
}
