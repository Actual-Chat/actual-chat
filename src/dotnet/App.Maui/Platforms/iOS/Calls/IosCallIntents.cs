using ActualChat.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using Foundation;
using Intents;

namespace ActualChat.App.Maui;

/// <summary>
/// Donates every call as an <see cref="INStartCallIntent"/> carrying the other party's Voxt
/// name and avatar. CallKit itself shows only Contacts photos, so this is the one route by
/// which a Voxt avatar reaches the system's call surfaces (Siri suggestions, Recents).
/// </summary>
public sealed class IosCallIntents(AppUIHub hub)
{
    private const int AvatarSize = 160;

    private AppUIHub Hub { get; } = hub;
    private IconUI IconUI => field ??= Hub.Services.GetRequiredService<IconUI>();
    private ILogger Log => field ??= StaticLog.For<IosCallIntents>();
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.IosCalls);

    public void Donate(ChatId chatId, bool hasVideo, INInteractionDirection direction)
        => _ = BackgroundTask.Run(
            () => DonateInternal(chatId, hasVideo, direction, Hub.StopToken),
            Log, $"Call intent donation failed for chat #{chatId}", Hub.StopToken);

    // Private methods

    private async Task DonateInternal(
        ChatId chatId, bool hasVideo, INInteractionDirection direction, CancellationToken cancellationToken)
    {
        var chat = await Hub.Chats.Get(Hub.Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return;

        var iconQuery = await GetIconQuery(chat, cancellationToken).ConfigureAwait(false);
        var loadedImage = await IconUI.Get(iconQuery, cancellationToken).ConfigureAwait(false);
        // Embedded rather than referenced by URL: iOS may purge the cache directory the file is in.
        using var image = loadedImage is null ? null : INImage.FromData(NSData.FromFile(loadedImage.FilePath));
        var person = new INPerson(
            personHandle: new INPersonHandle(chatId.Value, INPersonHandleType.Unknown),
            nameComponents: null,
            displayName: chat.Title,
            image: image,
            contactIdentifier: null,
            customIdentifier: chatId.Value);
        var intent = new INStartCallIntent(
            null,
            null,
            INCallAudioRoute.Unknown,
            INCallDestinationType.Normal,
            [person],
            hasVideo ? INCallCapability.VideoCall : INCallCapability.AudioCall);
        var interaction = new INInteraction(intent, null) {
            Direction = direction,
            Identifier = $"{chatId}-{Hub.Clocks.SystemClock.Now.EpochOffsetTicks}",
            GroupIdentifier = chatId.Value,
        };
        await interaction.DonateInteractionAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        DebugLog?.LogInformation("Donated {Direction} call intent for chat #{ChatId}", direction, chatId);
    }

    private async Task<IconQuery> GetIconQuery(Chat.Chat chat, CancellationToken cancellationToken)
    {
        // In a peer chat the other party is the same person whichever way the call went; a group's
        // own picture stands in for everyone else.
        if (chat.Id is not PeerChatId peerChatId)
            return chat.GetIconQuery(avatarSize: AvatarSize, renderAvatarTitle: true);

        var ownAccount = await Hub.AccountUI.OwnAccount.Use(cancellationToken).ConfigureAwait(false);
        var userId = peerChatId.UserIds.OtherThan(ownAccount.Id);
        var author = await Hub.Authors
            .GetByUserId(Hub.Session, chat.Id, userId, cancellationToken)
            .ConfigureAwait(false);
        return IconQuery.Create(
            author?.Avatar.Picture,
            AvatarKind.Beam,
            DefaultUserPicture.GetAvatarKey(userId.Value),
            AvatarSize);
    }
}
