namespace ActualChat.Users.Phone.Internal;

public sealed class CompositeTextMessageSender(IServiceProvider services) : ITextMessageSender
{
    private ILogger Log { get; } = services.LogFor<CompositeTextMessageSender>();

    [field: AllowNull, MaybeNull]
    private ITextMessageSender SmsTo => field ??= services.GetRequiredKeyedService<ITextMessageSender>("SMSTo");

    [field: AllowNull, MaybeNull]
    private ITextMessageSender Default => field ??= services.GetRequiredKeyedService<ITextMessageSender>("Default");

    public Task Send(ActualChat.Phone phone, string text)
    {
        var number = phone.E164Value;
        if (!number.StartsWith("+7", StringComparison.Ordinal))
            return Default.Send(phone, text);

        Log.LogDebug("Routing SMS via SMS.to for phone {Phone}", number);
        return SmsTo.Send(phone, text);

    }
}
