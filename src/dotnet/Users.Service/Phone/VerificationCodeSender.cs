using ActualChat.Resilience;

namespace ActualChat.Users.Phone;

public interface IVerificationCodeSender
{
    // Resolve deferred messages only after channel eligibility checks; null lets the caller try another channel.
    Task<TotpChannel?> Send(ActualChat.Phone phone, VerificationMessage message);
}

/// <summary>
/// A verification code and its text, optionally generated only once a channel can attempt delivery.
/// <see cref="OnlyChannel"/> restricts routing; <see cref="Source"/> supplies the SMS budget identities.
/// </summary>
public sealed record VerificationMessage(string Code, string Text, TotpChannel? OnlyChannel = null)
{
    private LazySlim<Task<VerificationMessage>>? MessageLazy { get; init; }
    public RateLimitSource Source { get; init; }

    public static VerificationMessage NewDeferred(
        Func<Task<VerificationMessage>> factory, TotpChannel? onlyChannel, RateLimitSource source)
        => new("", "", onlyChannel) { MessageLazy = LazySlim.New(factory), Source = source };

    public Task<VerificationMessage> Resolve() => MessageLazy?.Value ?? Task.FromResult(this);
}
