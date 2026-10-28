using ActualChat.Comparison;
using ActualChat.Contacts;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Per-chat contact data for UI code, all from one <see cref="IContacts.GetSummary"/> call per chat,
/// split here. Blazor Server takes the same path: one version of the code over a few saved local calls.
/// UserSettingsUI reads ChatUserSettings through it.
/// </summary>
public class ContactsUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub),
    IChatUserSettingsSource,
    IComputeService
{
    private IContacts Contacts => Hub.Contacts;

    // Get, GetLastOwnMention and GetChatUserSettings split one summary, so each is consolidated:
    // a reader of one part must not be invalidated by a change to another. Contact and Mention compare
    // by reference, and every summary brings new copies, hence the comparison by content.

    [ComputeMethod(ConsolidationDelay = 0, ConsolidationComparer = typeof(SerializedEqualityComparer<Contact>))]
    public virtual async Task<Contact?> Get(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var summary = await Contacts.GetSummary(Session, chatId, cancellationToken).ConfigureAwait(false);
        return summary?.Contact;
    }

    [ComputeMethod(ConsolidationDelay = 0, ConsolidationComparer = typeof(SerializedEqualityComparer<Mention>))]
    public virtual async Task<Mention?> GetLastOwnMention(
        ChatId chatId,
        CancellationToken cancellationToken = default)
    {
        var summary = await Contacts.GetSummary(Session, chatId, cancellationToken).ConfigureAwait(false);
        return summary?.LastOwnMention;
    }

    public async ValueTask<List<Contact>> ListUserContacts(CancellationToken cancellationToken = default)
    {
        var contactIds = await Contacts.ListIds(Session, null, cancellationToken).ConfigureAwait(false);
        var contacts = await contactIds
            .Select(cid => Get(cid.ChatId, cancellationToken))
            .Collect(ApiConstants.Concurrency.High, cancellationToken)
            .ConfigureAwait(false);
        return contacts.Where(c => c?.Account != null).ToList()!;
    }

    Task<ChatUserSettings?> IChatUserSettingsSource.Get(ChatId chatId, CancellationToken cancellationToken)
        => GetChatUserSettings(chatId, cancellationToken);

    // Protected/internal methods

    [ComputeMethod(ConsolidationDelay = 0)]
    protected virtual async Task<ChatUserSettings?> GetChatUserSettings(
        ChatId chatId,
        CancellationToken cancellationToken)
    {
        var summary = await Contacts.GetSummary(Session, chatId, cancellationToken).ConfigureAwait(false);
        return summary?.ChatUserSettings;
    }
}
