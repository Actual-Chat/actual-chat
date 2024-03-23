using ActualChat.Users.Module;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;

namespace ActualChat.Users;

public interface ITextMessageSender
{
    Task Send(Phone phone, string text);
}

public sealed class TwilioTextMessageSender(IServiceProvider services) : ITextMessageSender
{
    private UsersSettings UsersSettings { get; } = services.GetRequiredService<UsersSettings>();
    private ITwilioRestClient Client { get; } = services.GetRequiredService<ITwilioRestClient>();
    private ILogger Log { get; } = services.LogFor<TwilioTextMessageSender>();

    public async Task Send(Phone phone, string text)
    {
        try {
            await MessageResource
                .CreateAsync(new Twilio.Types.PhoneNumber(phone.ToInternational()),
                    from: UsersSettings.TwilioSmsFrom,
                    body: text,
                    client: Client)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to send text message");
            throw StandardError.External("We couldn't deliver the message to the specified phone number.");
        }
    }
}

public sealed class LogOnlyTextMessageSender(IServiceProvider services) : ITextMessageSender
{
    private ILogger Log { get; } = services.LogFor<LogOnlyTextMessageSender>();

    public Task Send(Phone phone, string text)
    {
        // just for debugging purpose
        Log.LogWarning("!!! Text message to {Phone}: {Text}", phone.ToInternational(), text);
        return Task.CompletedTask;
    }
}
