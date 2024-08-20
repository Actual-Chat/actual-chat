using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Time;
using ActualChat.Users.Email;
using ActualChat.Users.Templates;
using Mjml.Net;
using TimeZoneConverter;
using Unit = System.Reactive.Unit;

namespace ActualChat.Users;

public class EmailsBackend(IServiceProvider services) : IEmailsBackend
{
    private IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();
    private IChatPositionsBackend ChatPositionsBackend { get; } = services.GetRequiredService<IChatPositionsBackend>();
    private IContactsBackend ContactsBackend { get; } = services.GetRequiredService<IContactsBackend>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private IAuthorsBackend Authors { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IEmailSender EmailSender { get; } = services.GetRequiredService<IEmailSender>();
    private IServerKvasBackend ServerKvasBackend { get; } = services.GetRequiredService<IServerKvasBackend>();
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

        var timeZoneInfo = TZConvert.GetTimeZoneInfo(account.TimeZone);
        var userNow = TimeZoneInfo.ConvertTimeFromUtc(Clocks.SystemClock.Now, timeZoneInfo);
        var digestParameters = await FindUnreadChats(account, userNow, timeZoneInfo, cancellationToken).ConfigureAwait(false);
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
        DateTime userNow,
        TimeZoneInfo timeZoneInfo,
        CancellationToken cancellationToken)
    {
        const int takeChats = 5;
        var totalUnreadCount = 0;
        var unreadChats = new List<DigestParameters.DigestChat>();
        var accountSettings = ServerKvasBackend.GetUserClient(account.Id);
        var contactIds = await ContactsBackend
            .ListIdsForEntrySearch(account.Id, cancellationToken)
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

            var firstUnreadMessages = await ChatsBackend
                .ListNewEntries(contactId.ChatId, chatPosition.EntryLid, 1, cancellationToken)
                .ConfigureAwait(false);
            if (firstUnreadMessages.Count == 0)
                return default;

            var chatEntry = firstUnreadMessages[0];
            var author = await Authors
                .Get(chatEntry.ChatId, chatEntry.AuthorId, AuthorsBackend_GetAuthorOption.Full, cancellationToken)
                .ConfigureAwait(false);
            if (author is null)
                return default;

            var userBeginsAt = TimeZoneInfo.ConvertTimeFromUtc(chatEntry.BeginsAt.ToDateTime(), timeZoneInfo);
            var digestChat = new DigestParameters.DigestChat {
                Name = chat.Title,
                Link = UrlMapper.ToAbsolute(Links.Chat(chat.Id)),
                UnreadCount = maxEntryId - chatPosition.EntryLid,
                FirstUnreadChatEntry = new DigestParameters.DigestChatEntry {
                    At = DeltaText.Get(userBeginsAt, userNow).Text,
                    AuthorName = author.Avatar.Name,
                    Text = chatEntry.Content.IsNullOrEmpty()
                        ? "N/A"
                        : chatEntry.Content,
                },
            };
            return digestChat;
        }
    }
}
