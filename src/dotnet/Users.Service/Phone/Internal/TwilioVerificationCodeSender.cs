using ActualChat.Users.Module;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;

namespace ActualChat.Users.Phone.Internal;

public sealed class TwilioVerificationCodeSender(IServiceProvider services) : IVerificationCodeSender
{
    private UsersSettings UsersSettings { get; } = services.GetRequiredService<UsersSettings>();
    private ITwilioRestClient Client { get; } = services.GetRequiredService<ITwilioRestClient>();
    private ILogger Log { get; } = services.LogFor<TwilioVerificationCodeSender>();

    public async Task<TotpChannel?> Send(ActualChat.Phone phone, VerificationMessage message)
    {
        try {
            await MessageResource
                .CreateAsync(new Twilio.Types.PhoneNumber(phone.E164Value),
                    from: UsersSettings.TwilioSmsFrom,
                    body: message.Text,
                    client: Client)
                .ConfigureAwait(false);
        }
        // we intentionally don't send any errors to avoid malicious api usage
        // https://www.twilio.com/docs/api/errors/21211
        catch (Twilio.Exceptions.ApiException e) when (e.Code == 21211) {
            Log.LogDebug(e, "Failed to send text message. Invalid phone number");
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to send text message");
            throw Errors.DeliveryFailed();
        }

        return TotpChannel.Sms;
    }
}
