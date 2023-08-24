using ActualChat.Users.Module;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;

namespace ActualChat.Users;

public interface ISmsGateway
{
    Task Send(Phone phone, string text);
}

public class TwilioSmsGateway(IServiceProvider services) : ISmsGateway
{
    private ITwilioRestClient Client { get; } = services.GetRequiredService<ITwilioRestClient>();
    private UsersSettings Settings { get; } = services.GetRequiredService<UsersSettings>();
    private ILogger Log { get; } = services.LogFor<TwilioSmsGateway>();

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
            Log.LogError(e, "Failed to send sms");
            throw StandardError.External("Failed to deliver sms.", e);
        }
    }
}

public class LocalSmsGateway(IServiceProvider services) : ISmsGateway
{
    private ILogger Log { get; } = services.LogFor<LocalSmsGateway>();

    public Task Send(Phone phone, string text)
    {
        // just for debugging purpose
        Log.LogWarning("!!!!!!!!!!!! SMS send to {Phone}: {Text}", phone.ToInternational(), text);
        return Task.CompletedTask;
    }
}
