using ActualChat.Comparison;
using ActualChat.Contacts;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Per-chat contact data for UI code. A client gets all of it from one <see cref="IContacts.GetSummary"/>
/// RPC call per chat and splits it here; Blazor Server reads each part directly, since its calls are local.
/// UserSettingsUI reads ChatUserSettings through it on clients.
/// </summary>
public class ContactsUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub),
    IChatUserSettingsSource,
    IComputeService
{
    private IContacts Contacts => Hub.Contacts;
    private IMentions Mentions => Hub.Mentions;
    private bool IsBlazorServer { get; } = hub.HostInfo.HostKind.IsServer();

    // The public methods aren't compute methods: each routes to the one its host should depend on

    public Task<Contact?> Get(ChatId chatId, CancellationToken cancellationToken = default)
        => IsBlazorServer
            ? Contacts.GetForChat(Session, chatId, cancellationToken)
            : GetContactFromSummary(chatId, cancellationToken);

    public Task<Mention?> GetLastOwnMention(ChatId chatId, CancellationToken cancellationToken = default)
        => IsBlazorServer
            ? Mentions.GetLastOwn(Session, chatId, cancellationToken)
            : GetLastOwnMentionFromSummary(chatId, cancellationToken);

    public async ValueTask<List<Contact>> ListUserContacts(CancellationToken cancellationToken = default)
    {
        var contactIds = await Contacts.ListIds(Session, null, cancellationToken).ConfigureAwait(false);
        var contacts = await contactIds
            .Select(cid => Get(cid.ChatId, cancellationToken))
            .Collect(ApiConstants.Concurrency.High, cancellationToken)
            .ConfigureAwait(false);
        return contacts.Where(c => c?.Account != null).ToList()!;
    }

    // Registered as IChatUserSettingsSource on clients only, see BlazorUIAppModule
    Task<ChatUserSettings?> IChatUserSettingsSource.Get(ChatId chatId, CancellationToken cancellationToken)
        => GetChatUserSettingsFromSummary(chatId, cancellationToken);

    // Protected/internal methods

    // The three below split one summary, so each is consolidated: a reader of one part must not be
    // invalidated by a change to another. Contact and Mention compare by reference, and every
    // summary brings new copies, hence the comparison by content.

    [ComputeMethod(ConsolidationDelay = 0, ConsolidationComparer = typeof(SerializedEqualityComparer<Contact>))]
    protected virtual async Task<Contact?> GetContactFromSummary(ChatId chatId, CancellationToken cancellationToken)
    {
        var summary = await Contacts.GetSummary(Session, chatId, cancellationToken).ConfigureAwait(false);
        return summary?.Contact;
    }

    [ComputeMethod(ConsolidationDelay = 0, ConsolidationComparer = typeof(SerializedEqualityComparer<Mention>))]
    protected virtual async Task<Mention?> GetLastOwnMentionFromSummary(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var summary = await Contacts.GetSummary(Session, chatId, cancellationToken).ConfigureAwait(false);
        return summary?.LastOwnMention;
    }

    [ComputeMethod(ConsolidationDelay = 0)]
    protected virtual async Task<ChatUserSettings?> GetChatUserSettingsFromSummary(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var summary = await Contacts.GetSummary(Session, chatId, cancellationToken).ConfigureAwait(false);
        return summary?.ChatUserSettings;
    }
}
