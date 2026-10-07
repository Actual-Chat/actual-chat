using ActualChat.Users.Module;
using ActualLab.Generators;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;

namespace ActualChat.Users.Phone.Internal;

public sealed class TwilioVerificationCodeSender(IServiceProvider services) : IVerificationCodeSender
{
    private IServiceProvider Services { get; } = services;
    private TwilioMessageStatuses Statuses => field ??= Services.GetRequiredService<TwilioMessageStatuses>();
    private UsersSettings UsersSettings { get; } = services.GetRequiredService<UsersSettings>();
    private ITwilioRestClient Client { get; } = services.GetRequiredService<ITwilioRestClient>();
    private ILogger Log { get; } = services.LogFor<TwilioVerificationCodeSender>();

    public async Task<TotpChannel?> Send(ActualChat.Phone phone, VerificationMessage message)
    {
        try {
            string? attemptId = null;
            Uri? callbackUri = null;
            if (!UsersSettings.TwilioStatusCallbackUrl.IsNullOrEmpty()) {
                attemptId = RandomStringGenerator.Default.Next(32);
                callbackUri = Statuses.GetCallbackUri(attemptId);
                await Statuses.Begin(attemptId).ConfigureAwait(false);
            }

            message = await message.Resolve().ConfigureAwait(false);
            var result = await MessageResource
                .CreateAsync(new Twilio.Types.PhoneNumber(phone.E164Value),
                    from: UsersSettings.TwilioSmsFrom,
                    body: message.Text,
                    statusCallback: callbackUri,
                    client: Client)
                .ConfigureAwait(false);
            var isAccepted = result.Status == MessageResource.StatusEnum.Accepted
                || result.Status == MessageResource.StatusEnum.Queued
                || result.Status == MessageResource.StatusEnum.Sending
                || result.Status == MessageResource.StatusEnum.Sent
                || result.Status == MessageResource.StatusEnum.Delivered;
            if (attemptId is not null && !result.Sid.IsNullOrEmpty() && result.Status is not null) {
                try {
                    var isMatched = await Statuses.Update(attemptId, UsersSettings.TwilioAccountSid, result.Sid,
                        result.Status.ToString(), result.ErrorCode?.ToString()).ConfigureAwait(false);
                    if (!isMatched)
                        Log.LogWarning("Twilio message {MessageSid} did not match tracking attempt {AttemptId}",
                            result.Sid, attemptId);
                }
                catch (Exception e) {
                    Log.LogError(e, "Could not track Twilio message {MessageSid} for attempt {AttemptId}",
                        result.Sid, attemptId);
                }
            }
            if (result.Sid.IsNullOrEmpty() || result.ErrorCode is not null || !isAccepted)
                throw Errors.DeliveryFailed();

            Log.LogInformation("Twilio accepted verification SMS {MessageSid} with status {Status}",
                result.Sid, result.Status);
        }
        // we intentionally don't expose provider errors to clients
        // https://www.twilio.com/docs/api/errors/21211
        catch (Twilio.Exceptions.ApiException e) when (e.Code == 21211) {
            Log.LogDebug(e, "Failed to send text message. Invalid phone number");
            return null;
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to send text message");
            throw Errors.DeliveryFailed();
        }

        return TotpChannel.Sms;
    }
}
