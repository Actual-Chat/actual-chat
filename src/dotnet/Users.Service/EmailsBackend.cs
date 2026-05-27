using ActualChat.Chat.ML;
using ActualChat.Contacts;
using ActualChat.Users.Email;
using ActualChat.Users.Templates;
using Mjml.Net;
using Unit = System.Reactive.Unit;

namespace ActualChat.Users;

public class EmailsBackend(IServiceProvider services) : IEmailsBackend
{
    private IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();
    private IChatPositionsBackend ChatPositionsBackend { get; } = services.GetRequiredService<IChatPositionsBackend>();
    private IContactsBackend ContactsBackend { get; } = services.GetRequiredService<IContactsBackend>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private IEmailSender EmailSender { get; } = services.GetRequiredService<IEmailSender>();
    private IServerKvasBackend ServerKvasBackend { get; } = services.GetRequiredService<IServerKvasBackend>();
    private IChatDigestSummarizer ChatDigestSummarizer { get; } = services.GetRequiredService<IChatDigestSummarizer>();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private UrlMapper UrlMapper { get; } = services.UrlMapper();
    private ILogger Log { get; } = services.LogFor<EmailsBackend>();

    public virtual async Task<DigestPreview> GetDigestPreview(
        UserId userId, ChatId[] chatIds, DateTime? asOf, CancellationToken cancellationToken)
    {
        var userLanguage = await GetUserLanguage(userId, cancellationToken).ConfigureAwait(false);
        DigestParameters digestParameters;
        if (chatIds.Length > 0) {
            var now = asOf ?? Clocks.SystemClock.Now;
            digestParameters = await BuildSpecificChatsDigest(chatIds, now, userLanguage, cancellationToken).ConfigureAwait(false);
        }
        else {
            var account = await AccountsBackend
                .Get(userId, cancellationToken)
                .Require()
                .ConfigureAwait(false);
            digestParameters = await BuildUnreadChatsDigest(account, userLanguage, cancellationToken).ConfigureAwait(false);
        }

        var html = "";
        if (digestParameters.UnreadChats.Count > 0)
            html = await RenderDigest(digestParameters, cancellationToken).ConfigureAwait(false);
        var digestPreviewChats = digestParameters.UnreadChats
            .Select(c => new DigestPreviewChat {
                ChatId = c.Link, // link contains chat ID info
                Name = c.Name,
                Link = c.Link,
                UnreadCount = c.UnreadCount,
                BulletPoints = c.BulletPoints.ToArray(),
            })
            .ToArray();

        return new DigestPreview {
            Chats = digestPreviewChats,
            OtherUnreadCount = digestParameters.OtherUnreadCount,
            RenderedHtml = html,
        };
    }

    // [CommandHandler]
    public virtual async Task<Unit> OnSendDigest(EmailsBackend_SendDigest command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default;

        var isDiagnosticsEnabled = command.IsDiagnosticsEnabled;
        var diagLog = isDiagnosticsEnabled ? Log : null;
        diagLog?.LogInformation("-> OnSendDigest");

        var account = await AccountsBackend
            .Get(command.UserId, cancellationToken)
            .ConfigureAwait(false);
        if (account is null) {
            diagLog?.LogInformation("<- OnSendDigest. No account");
            return default;
        }

        var userLanguage = await GetUserLanguage(account.Id, cancellationToken).ConfigureAwait(false);
        var digestParameters = await BuildUnreadChatsDigest(account, userLanguage, cancellationToken).ConfigureAwait(false);
        if (digestParameters.UnreadChats.Count == 0) {
            diagLog?.LogInformation("<- OnSendDigest. No unread chats");
            return default;
        }

        var html = await RenderDigest(digestParameters, cancellationToken).ConfigureAwait(false);
        await EmailSender
            .Send("", account.Email, $"{CoreConstants.AppName}: digest", html, cancellationToken)
            .ConfigureAwait(false);

        diagLog?.LogInformation("<- OnSendDigest. Completed");
        return default;
    }

    private async Task<DigestParameters> BuildUnreadChatsDigest(AccountFull account, Language userLanguage, CancellationToken cancellationToken)
    {
        const int takeChats = 5;
        var totalUnreadCount = 0;
        var unreadChats = new List<DigestParameters.DigestChat>();
        var userSettings = ServerKvasBackend.ForUser(account.Id);
        var now = Clocks.SystemClock.Now;
        var contactIds = await ContactsBackend
            .ListIdsForSearch(account.Id, ContactSubset.All(), true, cancellationToken)
            .ConfigureAwait(false);

        foreach (var contactId in contactIds) {
            var digestChat = await BuildUnreadDigestChat(contactId).ConfigureAwait(false);
            if (digestChat is null)
                continue;

            totalUnreadCount++;
            if (unreadChats.Count <= takeChats)
                unreadChats.Add(digestChat);
        }

        return new DigestParameters {
            UnreadChats = unreadChats,
            OtherUnreadCount = totalUnreadCount - unreadChats.Count,
            OtherUnreadLink = UrlMapper.BaseUrl,
        };

        async Task<DigestParameters.DigestChat?> BuildUnreadDigestChat(ContactId contactId)
        {
            var chatId = contactId.ChatId;
            var chat = await ChatsBackend
                .Get(chatId, cancellationToken)
                .ConfigureAwait(false);
            if (chat is null)
                return null;

            // Notes chat should never appear as having unread messages
            if (chat.HasSingleAuthor)
                return default;

            var chatUserSettings = await userSettings.ChatUserSettings(chatId)
                .Get(cancellationToken)
                .ConfigureAwait(false);
            if (chatUserSettings.NotificationMode == ChatNotificationMode.Muted)
                return default;

            var chatPosition = await ChatPositionsBackend
                .Get(account.Id, chatId, ChatPositionKind.Read, cancellationToken)
                .ConfigureAwait(false);
            if (chatPosition.EntryLid <= 0)
                return default;

            var textEntryRange = await ChatsBackend
                .GetLidRange(chatId, false, cancellationToken)
                .ConfigureAwait(false);
            var maxEntryId = textEntryRange.End > 0 ? textEntryRange.End - 1 : 0;
            if (maxEntryId <= 0)
                return default;
            if (maxEntryId <= chatPosition.EntryLid)
                return default;

            var unreadCount = maxEntryId - chatPosition.EntryLid;
            return await BuildDigestChat(chatId, now, unreadCount, userLanguage, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<DigestParameters> BuildSpecificChatsDigest(
        IEnumerable<ChatId> chatIds,
        DateTime asOf,
        Language userLanguage,
        CancellationToken cancellationToken)
    {
        var unreadChats = new List<DigestParameters.DigestChat>();
        foreach (var chatId in chatIds) {
            var digestChat = await BuildDigestChat(chatId, asOf, 0, userLanguage, cancellationToken).ConfigureAwait(false);
            if (digestChat is not null)
                unreadChats.Add(digestChat);
        }
        return new DigestParameters {
            UnreadChats = unreadChats,
            OtherUnreadCount = 0,
            OtherUnreadLink = UrlMapper.BaseUrl,
        };
    }

    private async Task<DigestParameters.DigestChat?> BuildDigestChat(
        ChatId chatId, DateTime now, long unreadCount, Language userLanguage, CancellationToken cancellationToken)
    {
        var minBeginsAt = now + TimeSpan.FromDays(-1);
        var chat = await ChatsBackend
            .Get(chatId, cancellationToken)
            .ConfigureAwait(false);
        if (chat is null)
            return default;

        if (chat.Id is PlaceChatId { IsRoot: true })
            return default;

        var messages = await ChatsBackend
            .ListEntries(chatId, minBeginsAt, cancellationToken)
            .ConfigureAwait(false);
        if (!messages.Any())
            return default;

        var validMessages = messages
            .Where(x => !x.IsSystemEntry && !x.IsRemoved && !x.IsContentStreaming)
            .ToList();
        if (validMessages.Count == 0)
            return default;

        var hasText = validMessages.Any(x => !x.Content.IsNullOrEmpty());
        IReadOnlyCollection<string> bulletPoints;
        if (hasText) {
            var summarizable = validMessages
                .Select(WithMediaHint)
                .Where(x => !x.Content.IsNullOrEmpty())
                .ToList();
            bulletPoints = await ChatDigestSummarizer
                .Summarize(summarizable, cancellationToken)
                .ConfigureAwait(false);
        }
        else {
            var mediaEntries = validMessages.Where(x => x.Attachments.Length > 0).ToList();
            if (mediaEntries.Count == 0)
                return default;
            var summary = await ChatDigestSummarizer
                .SummarizeMediaShares(mediaEntries, userLanguage, cancellationToken)
                .ConfigureAwait(false);
            bulletPoints = summary.IsNullOrEmpty() ? [] : [summary];
        }
        if (bulletPoints.Count == 0)
            return default;

        return new DigestParameters.DigestChat {
            Name = chat.Title,
            Link = UrlMapper.ToAbsolute(Links.Chat(chat.Id)),
            UnreadCount = unreadCount,
            BulletPoints = bulletPoints,
        };
    }

    private async Task<Language> GetUserLanguage(UserId userId, CancellationToken cancellationToken)
    {
        var settings = await ServerKvasBackend
            .ForUser(userId)
            .UserLanguageSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        return settings.Primary;
    }

    private static ChatEntry WithMediaHint(ChatEntry entry)
    {
        if (!entry.Content.IsNullOrEmpty() || entry.Attachments.Length == 0)
            return entry;
        return entry with { Content = FormatMediaHint(entry.Attachments) };
    }

    // Language-neutral bracket form (e.g. "[image]", "[2 images and a video]", "[report.pdf]") so
    // that the LLM doesn't switch the summary's language to English on media-heavy chats.
    private static string FormatMediaHint(ChatEntryAttachment[] attachments)
    {
        var imageCount = 0;
        var videoCount = 0;
        ChatEntryAttachment? firstFile = null;
        foreach (var a in attachments)
            if (a.IsSupportedImage())
                imageCount++;
            else if (a.IsSupportedVideo())
                videoCount++;
            else
                firstFile ??= a;
        var fileCount = attachments.Length - imageCount - videoCount;

        var imagePart = imageCount switch {
            0 => "",
            1 => "image",
            _ => $"{imageCount} images",
        };
        var videoPart = videoCount switch {
            0 => "",
            1 => "video",
            _ => $"{videoCount} videos",
        };
        var filePart = fileCount switch {
            0 => "",
            1 => firstFile!.Media.FileName,
            _ => $"{fileCount} files",
        };
        var body = (imagePart.Length, videoPart.Length, filePart.Length) switch {
            (0, 0, _) => filePart,
            (0, _, 0) => videoPart,
            (_, 0, 0) => imagePart,
            (_, _, 0) => $"{imagePart} and {videoPart}",
            (_, 0, _) => $"{imagePart} and {filePart}",
            (0, _, _) => $"{videoPart} and {filePart}",
            _ => $"{imagePart}, {videoPart}, and {filePart}",
        };
        return $"[{body}]";
    }

    private static async Task<string> RenderDigest(DigestParameters digestParameters, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object?> {
            { nameof(Digest.Parameters), digestParameters },
        };
        var renderer = new BlazorRenderer();
        await using var _ = renderer.ConfigureAwait(false);
        var mjml = await renderer.RenderComponent<Digest>(parameters).ConfigureAwait(false);
        var mjmlRenderer = new MjmlRenderer();
        var mjmlOptions = new MjmlOptions { Beautify = false };
        var renderResult = await mjmlRenderer
            .RenderAsync(mjml, mjmlOptions, cancellationToken)
            .ConfigureAwait(false);
        return renderResult.Html;
    }
}
