using ActualChat.Diagnostics;
using ActualChat.Resilience;
using ActualChat.Users.Module;

namespace ActualChat.Users.Phone.Internal;

public sealed class CompositeVerificationCodeSender(IServiceProvider services) : IVerificationCodeSender
{
    private IServiceProvider Services { get; } = services;
    private RateLimitPolicy RateLimitPolicy => field ??= Services.GetRequiredService<RateLimitPolicy>();
    private UsersSettings Settings { get; } = services.GetRequiredService<UsersSettings>();
    private IVerificationCodeSender? Telegram { get; }
        = services.GetKeyedService<IVerificationCodeSender>("Telegram");
    private IVerificationCodeSender? SmsTo { get; } = services.GetKeyedService<IVerificationCodeSender>("SMSTo");
    private IVerificationCodeSender? Twilio { get; } = services.GetKeyedService<IVerificationCodeSender>("Twilio");
    private IVerificationCodeSender? LogOnly { get; }
        = services.GetKeyedService<IVerificationCodeSender>("LogOnly");
    private ILogger Log { get; } = services.LogFor<CompositeVerificationCodeSender>();
    private string[] SkipTelegramPhonePrefixes
        => field ??= Settings.SkipTelegramPhonePrefixes.Split([';', ','], StringSplitOptions.RemoveEmptyEntries);

    public async Task<TotpChannel?> Send(ActualChat.Phone phone, VerificationMessage message)
    {
        if (message.OnlyChannel == TotpChannel.Telegram)
            SkipChannel(TotpChannel.Sms, phone, "blocked");

        var sms = PickSmsSender(phone);
        var isSmsFirst = message.OnlyChannel != TotpChannel.Telegram && sms is not null
            && (message.OnlyChannel == TotpChannel.Sms || VerificationCodeRouting.PreferSms(phone, GetProvider(sms)));
        if (isSmsFirst) {
            var channel = await SendSms(sms, phone, message).ConfigureAwait(false);
            if (channel is not null)
                return channel;
        }

        if (message.OnlyChannel != TotpChannel.Sms) {
            var channel = await SendTelegram(phone, message).ConfigureAwait(false);
            if (channel is not null)
                return channel;
        }

        if (!isSmsFirst && message.OnlyChannel != TotpChannel.Telegram) {
            var channel = await SendSms(sms, phone, message).ConfigureAwait(false);
            if (channel is not null)
                return channel;
        }

        throw NoChannelLeft(phone, "no provider accepted the code");
    }

    // Private methods

    private async Task<TotpChannel?> SendSms(
        IVerificationCodeSender? sms, ActualChat.Phone phone, VerificationMessage message)
    {
        if (sms is null) {
            SkipChannel(TotpChannel.Sms, phone, "unconfigured");

            return null;
        }

        try {
            await CheckSmsLimits(phone, message).ConfigureAwait(false);
        }
        catch (RateLimitExceededException) {
            SkipChannel(TotpChannel.Sms, phone, "rate_limited", GetProvider(sms));
            throw;
        }

        return await TrySend(sms, TotpChannel.Sms, phone, message).ConfigureAwait(false);
    }

    private async Task CheckSmsLimits(ActualChat.Phone phone, VerificationMessage message)
    {
        var identities = new List<RateLimitIdentity> {
            new(RateLimitIdentityKind.Target, phone.E164Value),
        };
        if (message.Source.Session is { } session)
            identities.Add(new RateLimitIdentity(RateLimitIdentityKind.Session, session.Id));
        if (RateLimitIdentity.ForIP(message.Source.IPAddress) is { } ip)
            identities.Add(ip);

        await RateLimitPolicy.Check("VerificationCode.Sms", RateLimitClass.SmsSend,
            identities.ToArray()).ConfigureAwait(false);
        await RateLimitPolicy.Check("VerificationCode.Sms", RateLimitClass.SmsSendDaily,
            identities.ToArray()).ConfigureAwait(false);
    }

    private async Task<TotpChannel?> SendTelegram(ActualChat.Phone phone, VerificationMessage message)
    {
        // The prefix must be checked before calling Telegram.Send: a positive checkSendAbility is billed,
        // so a matching prefix has to skip the call entirely, not just discard its result.
        if (Telegram is null) {
            SkipChannel(TotpChannel.Telegram, phone, "unconfigured");

            return null;
        }
        if (IsTelegramSkipped(phone)) {
            SkipChannel(TotpChannel.Telegram, phone, "prefix", "telegram");

            return null;
        }

        return await TrySend(Telegram, TotpChannel.Telegram, phone, message).ConfigureAwait(false);
    }

    private Exception NoChannelLeft(ActualChat.Phone phone, string reason)
    {
        Log.LogError(
            "No channel accepted a verification code for {Phone}: {Reason}",
            phone.E164Value.ToPrivate(), reason);

        return Errors.DeliveryFailed();
    }

    private bool IsTelegramSkipped(ActualChat.Phone phone)
    {
        var value = phone.Normalize().Value;
        foreach (var prefix in SkipTelegramPhonePrefixes)
            if (value.StartsWith(prefix))
                return true;

        return false;
    }

    private async Task<TotpChannel?> TrySend(
        IVerificationCodeSender sender, TotpChannel channel, ActualChat.Phone phone, VerificationMessage message)
    {
        var provider = GetProvider(sender);
        try {
            var result = await sender.Send(phone, message).ConfigureAwait(false);
            if (result is null) {
                SkipChannel(channel, phone, "declined", provider);

                return null;
            }

            AppMeters.VerificationCodeSent.Add(1, ChannelTag(channel), CountryTag(phone), ProviderTag(provider));
            Log.LogInformation("Verification code accepted by {Provider} via {Channel} for country code {CountryCode}",
                provider, channel, CountryTag(phone).Value);

            return result;
        }
        catch (Exception e) {
            SkipChannel(channel, phone, "failed", provider);
            Log.LogError(e,
                "{Provider} failed to accept a verification code via {Channel}",
                provider, channel);

            // A failed request may already have been accepted; sending via another channel could double-send.
            throw Errors.DeliveryFailed();
        }
    }

    private IVerificationCodeSender? PickSmsSender(ActualChat.Phone phone)
    {
        if (SmsTo is { } smsTo && phone.E164Value.StartsWith("+7"))
            return smsTo;

        return Twilio ?? SmsTo ?? LogOnly;
    }

    private string GetProvider(IVerificationCodeSender sender)
        => ReferenceEquals(sender, Telegram) ? "telegram"
            : ReferenceEquals(sender, SmsTo) ? "smsto"
            : ReferenceEquals(sender, Twilio) ? "twilio"
            : "log_only";

    private static void SkipChannel(
        TotpChannel channel, ActualChat.Phone phone, string reason, string provider = "none")
        => AppMeters.VerificationCodeChannelSkipped.Add(
            1, ChannelTag(channel), CountryTag(phone), ReasonTag(reason), ProviderTag(provider));

    private static KeyValuePair<string, object?> ChannelTag(TotpChannel channel)
        => new("channel", channel.ToString());

    private static KeyValuePair<string, object?> CountryTag(ActualChat.Phone phone)
        => new("country", PhoneCodes.GetByCode(phone.Code) is null ? "other" : phone.Code);

    private static KeyValuePair<string, object?> ProviderTag(string provider)
        => new("provider", provider);

    private static KeyValuePair<string, object?> ReasonTag(string reason)
        => new("reason", reason);
}
