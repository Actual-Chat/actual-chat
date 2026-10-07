namespace ActualChat.Users.Phone.Internal;

public sealed class LogOnlyVerificationCodeSender(IServiceProvider services) : IVerificationCodeSender
{
    private ILogger Log { get; } = services.LogFor<LogOnlyVerificationCodeSender>();

    public async Task<TotpChannel?> Send(ActualChat.Phone phone, VerificationMessage message)
    {
        message = await message.Resolve().ConfigureAwait(false);
        Log.LogWarning("!!! Verification code to {Phone}: {Text}", phone.E164Value, message.Text.ToPrivate());

        return TotpChannel.Sms;
    }
}
