using ActualChat.Chat;
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

    // [CommandHandler]
    public virtual async Task<Unit> OnSendDigest(EmailsBackend_SendDigest command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default;

        var account = await AccountsBackend
            .Get(command.UserId, cancellationToken)
            .ConfigureAwait(false);
        if (account is null)
            return default;

        var digestParameters = await FindUnreadChats(account, cancellationToken).ConfigureAwait(false);
        if (digestParameters.UnreadChats.Count == 0)
            return default;

        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal) {
            { nameof(Digest.Parameters), digestParameters },
        };
        var renderer = new BlazorRenderer();
        await using var _ = renderer.ConfigureAwait(false);
        var mjml = await renderer.RenderComponent<Digest>(parameters).ConfigureAwait(false);
        var mjmlRenderer = new MjmlRenderer();
        var mjmlOptions = new MjmlOptions {
            Minify = true,
            Beautify = false,
        };
        var renderResult = mjmlRenderer.Render(mjml, mjmlOptions);

        await EmailSender.Send(
                "",
                account.Email,
                "Actual Chat: digest",
                renderResult.Html,
                cancellationToken)
            .ConfigureAwait(false);

        return default;
    }

    private async Task<DigestParameters> FindUnreadChats(
        AccountFull account,
        CancellationToken cancellationToken)
    {
        const int takeChats = 5;
        var totalUnreadCount = 0;
        var unreadChats = new List<DigestParameters.DigestChat>();
        var accountSettings = ServerKvasBackend.GetUserClient(account.Id);
        var contactIds = await ContactsBackend
            .ListIdsForSearch(account.Id, null, true, cancellationToken)
            .ConfigureAwait(false);
        foreach (var contactId in contactIds) {
            var digestChat = await GetDigestChat(contactId).ConfigureAwait(false);
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

        async Task<DigestParameters.DigestChat?> GetDigestChat(ContactId contactId)
        {
            var userChatSettings = await accountSettings
                .GetUserChatSettings(contactId.ChatId, cancellationToken)
                .ConfigureAwait(false);
            if (userChatSettings.NotificationMode == ChatNotificationMode.Muted)
                return default;

            var chatPosition = await ChatPositionsBackend
                .Get(account.Id, contactId.ChatId, ChatPositionKind.Read, cancellationToken)
                .ConfigureAwait(false);
            if (chatPosition.EntryLid <= 0)
                return default;

            var textEntryRange = await ChatsBackend
                .GetIdRange(contactId.ChatId, ChatEntryKind.Text, false, cancellationToken)
                .ConfigureAwait(false);
            var maxEntryId = textEntryRange.End > 0 ? textEntryRange.End - 1 : 0;
            if (maxEntryId <= 0)
                return default;
            if (maxEntryId <= chatPosition.EntryLid)
                return default;

            var chat = await ChatsBackend
                .Get(contactId.ChatId, cancellationToken)
                .ConfigureAwait(false);
            if (chat is null)
                return default;

            if (chat.Id.IsPlaceRootChat)
                return default;

            var messages = await ChatsBackend
                .ListEntries(contactId.ChatId, Clocks.SystemClock.Now + TimeSpan.FromDays(-1), cancellationToken)
                .ConfigureAwait(false);
            if (messages.Count == 0)
                return default;

            var nonSystemMessages = messages.Where(x => !x.IsSystemEntry).ToList();
            if (nonSystemMessages.Count == 0)
                return default;

            var bulletPoints = await ChatDigestSummarizer.Summarize(nonSystemMessages, cancellationToken).ConfigureAwait(false);
            if (bulletPoints.Count == 0)
                return default;

            var digestChat = new DigestParameters.DigestChat {
                Name = chat.Title,
                Link = UrlMapper.ToAbsolute(Links.Chat(chat.Id)),
                UnreadCount = maxEntryId - chatPosition.EntryLid,
                BulletPoints = bulletPoints,
            };
            return digestChat;
        }
    }
}
