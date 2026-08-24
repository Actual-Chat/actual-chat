namespace ActualChat.Users.Phone;

public interface IVerificationCodeSender
{
    // Returns null when this channel can't reach the phone - the caller is expected to try another one
    Task<TotpChannel?> Send(ActualChat.Phone phone, VerificationMessage message);
}

/// <summary>
/// A verification code together with the ready-to-send text. Channels delivering arbitrary text use
/// <see cref="Text"/>; channels delivering the code itself (Telegram) use <see cref="Code"/> and ignore the text.
/// <see cref="OnlyChannel"/> narrows the cascade to a single channel when the others can't serve the number.
/// </summary>
public sealed record VerificationMessage(string Code, string Text, TotpChannel? OnlyChannel = null);
