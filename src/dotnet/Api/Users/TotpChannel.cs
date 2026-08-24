namespace ActualChat.Users;

/// <summary>
/// Specifies the channel a time-based one-time password (TOTP) was delivered through.
/// </summary>
public enum TotpChannel
{
    Telegram,
    Sms,
}
