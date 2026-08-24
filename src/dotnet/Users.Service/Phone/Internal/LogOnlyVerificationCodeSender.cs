namespace ActualChat.Users.Phone.Internal;

public sealed class LogOnlyVerificationCodeSender(IServiceProvider services) : IVerificationCodeSender
{
    private ILogger Log { get; } = services.LogFor<LogOnlyVerificationCodeSender>();

    public Task<TotpChannel?> Send(ActualChat.Phone phone, VerificationMessage message)
    {
        // just for debugging purpose
        Log.LogWarning("!!! Verification code to {Phone}: {Text}", phone.E164Value, message.Text.ToPrivate());

        return Task.FromResult<TotpChannel?>(TotpChannel.Sms);
    }
}
