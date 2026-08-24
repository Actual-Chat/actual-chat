using ActualChat.Diagnostics;
using ActualChat.Users.Module;

namespace ActualChat.Users.Phone.Internal;

public sealed class CompositeVerificationCodeSender(IServiceProvider services) : IVerificationCodeSender
{
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
        // SMS goes first: it reaches any phone, while Telegram reaches only numbers that have an account
        // and someone reading it. Telegram is the way out where SMS can't serve the number at all.
        if (message.OnlyChannel == TotpChannel.Telegram)
            SkipChannel(TotpChannel.Sms, phone, "blocked");
        else if (PickSmsSender(phone) is { } sms) {
            var channel = await TrySend(sms, TotpChannel.Sms, phone, message).ConfigureAwait(false);
            if (channel is not null)
                return channel;
        }
        else
            SkipChannel(TotpChannel.Sms, phone, "unconfigured");

        return await SendTelegram(phone, message).ConfigureAwait(false);
    }

    // Private methods

    private async Task<TotpChannel> SendTelegram(ActualChat.Phone phone, VerificationMessage message)
    {
        // The prefix must be checked before calling Telegram.Send: a positive checkSendAbility is billed,
        // so a matching prefix has to skip the call entirely, not just discard its result.
        if (Telegram is null || IsTelegramSkipped(phone)) {
            SkipChannel(TotpChannel.Telegram, phone, "prefix");

            throw Errors.DeliveryFailed();
        }

        var channel = await TrySend(Telegram, TotpChannel.Telegram, phone, message).ConfigureAwait(false);
        if (channel is null)
            throw Errors.DeliveryFailed();

        return channel.Value;
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
        try {
            var result = await sender.Send(phone, message).ConfigureAwait(false);
            if (result is null) {
                SkipChannel(channel, phone, "declined");

                return null;
            }

            AppMeters.VerificationCodeSent.Add(1, ChannelTag(channel), CountryTag(phone));

            return result;
        }
        catch (Exception e) {
            SkipChannel(channel, phone, "failed");
            Log.LogError(e,
                "{Channel} failed to deliver a verification code to {Phone}, falling back",
                channel, phone.E164Value.ToPrivate());

            return null;
        }
    }

    private IVerificationCodeSender? PickSmsSender(ActualChat.Phone phone)
    {
        if (SmsTo is { } smsTo && phone.E164Value.StartsWith("+7"))
            return smsTo;

        return Twilio ?? SmsTo ?? LogOnly;
    }

    private static void SkipChannel(TotpChannel channel, ActualChat.Phone phone, string reason)
        => AppMeters.VerificationCodeChannelSkipped.Add(
            1, ChannelTag(channel), CountryTag(phone), ReasonTag(reason));

    private static KeyValuePair<string, object?> ChannelTag(TotpChannel channel)
        => new("channel", channel.ToString());

    private static KeyValuePair<string, object?> CountryTag(ActualChat.Phone phone)
        => new("country", PhoneCodes.GetByCode(phone.Code) is null ? "other" : phone.Code);

    private static KeyValuePair<string, object?> ReasonTag(string reason)
        => new("reason", reason);
}
