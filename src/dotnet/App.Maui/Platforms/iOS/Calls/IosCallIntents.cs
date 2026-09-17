using ActualChat.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using Intents;

namespace ActualChat.App.Maui;

/// <summary>
/// Donates every call as an <see cref="INStartCallIntent"/> carrying the other party's Voxt
/// name and avatar, for Siri and Spotlight suggestions. The ring and Recents never see it:
/// callservicesd builds those from Contacts alone.
/// </summary>
public sealed class IosCallIntents(AppUIHub hub)
{
    private AppUIHub Hub { get; } = hub;
    private IconUI IconUI => field ??= Hub.Services.GetRequiredService<IconUI>();
    private ILogger Log => field ??= Hub.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.IosCalls);

    public void Donate(ChatId chatId, bool hasVideo, INInteractionDirection direction)
        => _ = BackgroundTask.Run(
            () => DonateInternal(chatId, hasVideo, direction, Hub.StopToken),
            Log, $"Call intent donation failed for chat #{chatId}", Hub.StopToken);

    // Private methods

    private async Task DonateInternal(
        ChatId chatId, bool hasVideo, INInteractionDirection direction, CancellationToken cancellationToken)
    {
        var contact = await Hub.Contacts.GetForChat(Hub.Session, chatId, cancellationToken).ConfigureAwait(false);
        if (contact is null)
            return;

        using var image = await IconUI.GetIntentImage(contact, cancellationToken).ConfigureAwait(false);
        var intent = new INStartCallIntent(
            null,
            null,
            INCallAudioRoute.Unknown,
            INCallDestinationType.Normal,
            [ChatIntents.NewPerson(contact.Chat, image)],
            hasVideo ? INCallCapability.VideoCall : INCallCapability.AudioCall);
        var now = Hub.Clocks.SystemClock.Now;
        await ChatIntents.Donate(intent, chatId, direction, now, cancellationToken).ConfigureAwait(false);
        DebugLog?.LogInformation("Donated {Direction} call intent for chat #{ChatId}, hasImage={HasImage}",
            direction, chatId, image is not null);
    }
}
