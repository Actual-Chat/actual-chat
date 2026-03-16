using ActualChat.Hosting;
using ActualChat.Module;
using ActualChat.Notification.Module;
using dotAPNS;
using dotAPNS.AspNetCore;

namespace ActualChat.Notification;

public class Apns(IApnsClientFactory clientFactory, HostInfo hostInfo, NotificationSettings settings, CoreServerSettings coreServerSettings, ILogger<Apns> log)
{
    [field: AllowNull, MaybeNull]
    private IApnsClient Client => field ??= clientFactory.CreateUsingJwt(new ApnsJwtOptions {
            KeyId = settings.Apns.KeyId,
            CertFilePath = settings.Apns.KeyPath.RequireFileExists(),
            BundleId = coreServerSettings.AppleAppId,
            TeamId = coreServerSettings.AppleTeamId,
        },
        !hostInfo.IsProductionInstance);

    public async Task SendVoip(
        ChatId chatId,
        IReadOnlyList<Symbol> deviceIds,
        string title,
        Phone? phone,
        string? email,
        CancellationToken cancellationToken)
    {
        log.LogInformation("Sending {Count} Apple VoIP pushes", deviceIds.Count);
        var responses = await deviceIds.Select(CreatePush).Select(x => Client.SendAsync(x, cancellationToken)).Collect(cancellationToken).ConfigureAwait(false);
        var failedResponses = responses.Where(x => !x.IsSuccessful).ToList();
        if (failedResponses.Count > 0)
            foreach (var responseGroup in failedResponses.GroupBy(x => x.Reason))
                log.LogError("{Count} Apple VoIP pushes failed with {Reason}: {ReasonString}",
                    responseGroup.Count(),
                    responseGroup.Key,
                    responseGroup.First().ReasonString);
        return;

        ApplePush CreatePush(Symbol deviceId)
            => new ApplePush(ApplePushType.Voip).AddVoipToken(deviceId)
                .AddCustomProperty("uuid", Guid.NewGuid())
                .Add("callerName", title)
                .Add("phone", phone?.E164Value)
                .Add("email", email)
                .Add("chatId", chatId.Value)
                .AddCustomProperty("hasVideo", false)
                .AddContentAvailable()
                .AddImmediateExpiration()
                .SetPriority(10);
    }
}
