using ActualChat.Users.Module;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;

namespace ActualChat.Users;

public interface ITextMessageGateway
{
    Task Send(Phone phone, string text);
}

public class TwilioTextMessageGateway(IServiceProvider services) : ITextMessageGateway
{
    private ITwilioRestClient Client { get; } = services.GetRequiredService<ITwilioRestClient>();
    private UsersSettings Settings { get; } = services.GetRequiredService<UsersSettings>();
    private ILogger Log { get; } = services.LogFor<TwilioTextMessageGateway>();

    public async Task Send(Phone phone, string text)
    {
        try {
            await MessageResource
                .CreateAsync(new Twilio.Types.PhoneNumber(phone.ToInternational()),
                    from: Settings.TwilioSmsFrom,
                    body: text,
                    client: Client)
                .ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to send test message");
            throw StandardError.External("We couldn't deliver text message to this phone number. Please try to use another authentication method.");
        }
    }
}

public class LocalTextMessageGateway(IServiceProvider services) : ITextMessageGateway
{
    private ILogger Log { get; } = services.LogFor<LocalTextMessageGateway>();

    public Task Send(Phone phone, string text)
    {
        // just for debugging purpose
        Log.LogWarning("!!!!!!!!!!!! Text message send to {Phone}: {Text}", phone.ToInternational(), text);
        return Task.CompletedTask;
    }
}
