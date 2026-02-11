using ActualChat.Chat;
using ActualChat.Contacts;

namespace ActualChat.UI.App.Services;

public class IncomingShareSuggestions(IServiceProvider services)
{
    protected IServiceProvider Services { get; } = services;
    protected IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    protected Session Session => field ??= Services.GetRequiredService<Session>();
    protected ILogger Log => field ??= Services.LogFor(GetType());
    protected IContacts Contacts => field ??= Services.GetRequiredService<IContacts>();

    public void Push(ChatId chatId)
        => _ = Suggest(chatId);

    // TODO: throttling
    public async Task Suggest(ChatId chatId, CancellationToken cancellationToken = default)
    {
        try {
            var ownAccount = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
            var contactId = ContactId.NewAny(ownAccount.Id, chatId);
            await SuggestInternal(contactId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to suggest incoming share to chat #{ChatId}", chatId);
        }
    }

    // TODO: throttling
    public Task Suggest(ContactId contactId, CancellationToken cancellationToken = default)
        => SuggestInternal(contactId, cancellationToken);

    protected virtual Task SuggestInternal(ContactId contactId, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
