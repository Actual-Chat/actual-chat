using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Search;
using ActualChat.Users;

namespace ActualChat.MLSearch.Indexing;

public sealed class UserContactIndexer(IServiceProvider services, IAccountsBackend accountsBackend, IAuthorsBackend authorsBackend, IContactsBackend contactsBackend) : ContactIndexer(services)
{
    protected override async Task Sync(CancellationToken cancellationToken)
    {
        if (await SyncChanges(cancellationToken).ConfigureAwait(false))
            await Commander.Call(new SearchBackend_Refresh(true), cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> SyncChanges(CancellationToken cancellationToken)
    {
        var hasAccountChanges = await SyncAccountChanges(cancellationToken).ConfigureAwait(false);
        var hasPlaceAuthorChanges = await SyncPlaceAuthorChanges(cancellationToken).ConfigureAwait(false);
        return hasAccountChanges || hasPlaceAuthorChanges;
    }

    private async Task<bool> SyncAccountChanges(CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Region();
        var state = await ContactIndexStatesBackend.GetForUsers(cancellationToken).ConfigureAwait(false);
        var batches = accountsBackend
            .BatchChanged(state.LastUpdatedVersion,
                MaxVersion,
                state.LastUpdatedUserId,
                SyncBatchSize,
                cancellationToken);
        var hasChanges = false;
        await foreach (var accounts in batches.ConfigureAwait(false)) {
            using var _2 = Tracer.Region($"{nameof(SyncChanges)} batch: {accounts.Count} accounts");
            var first = accounts[0];
            var last = accounts[^1];
            Log.LogDebug(
                "Indexing {BatchSize} user contacts [(v={FirstVersion}, #{FirstId})..(v={LastVersion}, #{LastId})]",
                accounts.Count,
                first.Version,
                first.Id,
                last.Version,
                last.Id);
            NeedsSync.Reset();
            var updates = await accounts.Select(ToIndexedUserContact).Collect().ToApiArray().ConfigureAwait(false);
            var indexCmd = new SearchBackend_UserContactBulkIndex(updates, []);
            await Commander.Call(indexCmd, cancellationToken).ConfigureAwait(false);

            state = state with { LastUpdatedId = last.Id, LastUpdatedVersion = last.Version };
            state = await SaveState(state, cancellationToken).ConfigureAwait(false);
            hasChanges = true;
        }
        return hasChanges;

        async Task<IndexedUserContact> ToIndexedUserContact(AccountFull account)
        {
            var placeIds = await contactsBackend.ListPlaceIds(account.Id, cancellationToken).ConfigureAwait(false);
            return account.ToIndexedUserContact(placeIds);
        }
    }

    private async Task<bool> SyncPlaceAuthorChanges(CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Region();
        var state = await ContactIndexStatesBackend.GetForPlaceAuthors(cancellationToken).ConfigureAwait(false);
        var batches = authorsBackend
            .BatchChangedPlaceAuthors(state.LastUpdatedVersion,
                MaxVersion,
                state.LastUpdatedPlaceAuthorId,
                SyncBatchSize,
                cancellationToken);
        var hasChanges = false;
        await foreach (var authors in batches.ConfigureAwait(false)) {
            using var _2 = Tracer.Region($"{nameof(SyncChanges)} batch: {authors.Count} authors");
            var first = authors[0];
            var last = authors[^1];
            Log.LogDebug(
                "Indexing {BatchSize} user contacts [(v={FirstVersion}, #{FirstId})..(v={LastVersion}, #{LastId})]",
                authors.Count,
                first.Version,
                first.Id,
                last.Version,
                last.Id);
            NeedsSync.Reset();
            var userContacts = await authors.Select(x => x.UserId).Distinct().Select(ToIndexedUserContact).Collect().ConfigureAwait(false);
            var updates = userContacts.SkipNullItems().ToApiArray();
            var indexCmd = new SearchBackend_UserContactBulkIndex(updates, []);
            await Commander.Call(indexCmd, cancellationToken).ConfigureAwait(false);

            state = state with { LastUpdatedId = last.Id, LastUpdatedVersion = last.Version };
            state = await SaveState(state, cancellationToken).ConfigureAwait(false);
            hasChanges = true;
        }
        return hasChanges;

        async Task<IndexedUserContact?> ToIndexedUserContact(UserId userId)
        {
            var account = await accountsBackend.Get(userId, cancellationToken).ConfigureAwait(false);
            if (account is null)
                return null;

            var placeIds = await contactsBackend.ListPlaceIds(account.Id, cancellationToken).ConfigureAwait(false);
            return account.ToIndexedUserContact(placeIds);
        }
    }
}
