using ActualChat.Users.Module;
using Twilio;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;

namespace ActualChat.Users;

public interface ISmsGateway
{
    Task Send(Phone phone, string text);
}

public class TwilioSmsGateway : ISmsGateway
{
    private ITwilioRestClient Client { get; }
    private UsersSettings Settings { get; }

    public TwilioSmsGateway(ITwilioRestClient client, UsersSettings settings)
    {
        Client = client;
        Settings = settings;
    }

    public async Task Send(Phone phone, string text)
    {
        TwilioClient.GetRestClient();
        await MessageResource
            .CreateAsync(new Twilio.Types.PhoneNumber(phone.ToInternational()),
                from: Settings.TwilioSmsFrom,
                body: text,
                client: Client)
            .ConfigureAwait(false);
    }
}

public class LocalSmsGateway : ISmsGateway
{
    public ILogger<LocalSmsGateway> Log { get; }

    public LocalSmsGateway(ILogger<LocalSmsGateway> log)
        => Log = log;

    public Task Send(Phone phone, string text)
    {
        // just for debugging purpose
        Log.LogWarning("!!!!!!!!!!!! SMS send to {Phone}: {Text}", phone.ToInternational(), text);
        return Task.CompletedTask;
    }
}
