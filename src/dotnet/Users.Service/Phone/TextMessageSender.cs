namespace ActualChat.Users.Phone;

public interface ITextMessageSender
{
    Task Send(ActualChat.Phone phone, string text);
}

public sealed class LogOnlyTextMessageSender(IServiceProvider services) : ITextMessageSender
{
    private ILogger Log { get; } = services.LogFor<LogOnlyTextMessageSender>();

    public Task Send(ActualChat.Phone phone, string text)
    {
        // just for debugging purpose
        Log.LogWarning("!!! Text message to {Phone}: {Text}", phone.E164Value, text);
        return Task.CompletedTask;
    }
}
