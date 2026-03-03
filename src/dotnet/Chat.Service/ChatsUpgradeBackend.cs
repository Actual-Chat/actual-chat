using ActualChat.Chat.Db;
using ActualChat.Contacts;
using ActualChat.Media;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using ActualLab.Fusion.EntityFramework;

namespace ActualChat.Chat;

public partial class ChatsUpgradeBackend : DbServiceBase<ChatDbContext>, IChatsUpgradeBackend
{
    private ChatsBackend ChatsBackend { get; }
    private IAccountsBackend AccountsBackend { get; }
    private IAuthorsBackend AuthorsBackend { get; }
    private IAuthorsUpgradeBackend AuthorsUpgradeBackend { get; }
    private IRolesBackend RolesBackend { get; }
    private IContactsBackend ContactsBackend { get; }
    private IBlobStorages Blobs { get; }
    private IMediaBackend MediaBackend { get; }

    public ChatsUpgradeBackend(IServiceProvider services) : base(services)
    {
        ChatsBackend = (ChatsBackend)services.GetRequiredService<IChatsBackend>();
        AccountsBackend = services.GetRequiredService<IAccountsBackend>();
        AuthorsBackend = services.GetRequiredService<IAuthorsBackend>();
        AuthorsUpgradeBackend = services.GetRequiredService<IAuthorsUpgradeBackend>();
        RolesBackend = services.GetRequiredService<IRolesBackend>();
        ContactsBackend = services.GetRequiredService<IContactsBackend>();
        Blobs = Services.GetRequiredService<IBlobStorages>();
        MediaBackend = services.GetRequiredService<IMediaBackend>();
    }

    // [CommandHandler]
    public virtual async Task OnUpgradeChat(
        ChatsUpgradeBackend_UpgradeChat command,
        CancellationToken cancellationToken)
    {
        // NOTE(AY): Currently this command just "repairs" some of chat properties,
        // even though originally it was upgrading DbChat.Owners to roles & authors.
        //
        // This part isn't there anymore, coz Owners are gone,
        // and there is no code calling this command.
        //
        // I left it here mainly "just in case" - e.g. if in future we'll end up using
        // exactly the same command to perform chat upgrades (though migrations are
        // certainly preferable for that).

        var chatId = command.ChatId.Require();
        var context = CommandContext.GetCurrent();

        if (Invalidation.IsActive) {
            var invChat = context.Operation.Items.KeylessGet<Chat>()!;
            _ = ChatsBackend.Get(invChat.Id, default);
            return;
        }

        var dbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var __ = dbContext.ConfigureAwait(false);

        var dbChat = await dbContext.Chats
            .SingleOrDefaultAsync(c => c.Id == chatId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (dbChat == null)
            return;

        Log.LogInformation("Upgrading chat #{ChatId}: '{ChatTitle}' ({ChatType})",
            chatId, dbChat.Title, dbChat.Kind);

        var chat = dbChat.ToModel();
        if (chat.Id is PeerChatId peerChatId) {
            // Peer chat
            await peerChatId.UserIds
                .ToArray()
                .Select(userId => AuthorsBackend.EnsureJoined(chatId, userId, cancellationToken))
                .Collect(cancellationToken)
                .ConfigureAwait(false);
        }
        else {
            // Group chat

            // Removing duplicate system roles
            var systemDbRoles = await dbContext.Roles
                .Where(r => r.ChatId == chatId.Value && r.SystemRole != SystemRole.None)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var group in systemDbRoles.GroupBy(r => r.SystemRole)) {
                if (group.Count() <= 1)
                    continue;
                foreach (var dbChatRole in group.Skip(1))
                    dbContext.Roles.Remove(dbChatRole);
            }
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Reload system roles
            systemDbRoles = await dbContext.Roles
                .Where(r => r.ChatId == chatId.Value && r.SystemRole != SystemRole.None)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var dbAnyoneRole = systemDbRoles.SingleOrDefault(r => r.SystemRole == SystemRole.Anyone);
            if (dbAnyoneRole == null) {
                var createAnyoneRoleCmd = new RolesBackend_Change(chatId, default, null, new() {
                    Create = new RoleDiff() {
                        SystemRole = SystemRole.Anyone,
                        Permissions =
                            ChatPermissions.Write
                            | ChatPermissions.Invite
                            | ChatPermissions.SeeMembers
                            | ChatPermissions.Leave,
                    },
                });
                await Commander.Call(createAnyoneRoleCmd, cancellationToken).ConfigureAwait(false);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        chat = dbChat.ToModel();
        context.Operation.Items.KeylessSet(chat);
    }

    // [CommandHandler]
    public virtual async Task OnMigrateAudioEntries(
        ChatsUpgradeBackend_MigrateAudioEntries command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        const int batchSize = 100;
        var totalMigrated = 0;
        long lastProcessedLocalId = 0;

        Log.LogInformation("Starting audio entry migration...");

        while (!cancellationToken.IsCancellationRequested) {
            var dbContext = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
            await using var _ = dbContext.ConfigureAwait(false);

            // Find text entries that reference audio entries but haven't been migrated yet
            var textEntries = await dbContext.ChatEntries
                .Where(e => e.Kind == 0)
                .Where(e => e.AudioEntryId != null)
                .Where(e => e.AudioId == null || e.AudioId == "")
                .Where(e => e.LocalId > lastProcessedLocalId)
                .OrderBy(e => e.ChatId).ThenBy(e => e.LocalId)
                .Take(batchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (textEntries.Count == 0)
                break;

            foreach (var textEntry in textEntries) {
                lastProcessedLocalId = textEntry.LocalId;

                // Look up the corresponding audio entry (legacy format: chatId:1:localId)
                var audioEntryIdStr = $"{textEntry.ChatId}:1:{textEntry.AudioEntryId!.Value}";

                var audioDbEntry = await dbContext.ChatEntries
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        e => e.Id == audioEntryIdStr,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (audioDbEntry is null) {
                    Log.LogWarning(
                        "Audio entry {AudioEntryId} not found for text entry {ChatEntryId}, skipping",
                        audioEntryIdStr, textEntry.Id);
                    continue;
                }

                var audioBlobId = audioDbEntry.Content;
                if (audioBlobId.IsNullOrEmpty()) {
                    Log.LogWarning(
                        "Audio entry {AudioEntryId} has no content (blob ID), skipping",
                        audioEntryIdStr);
                    continue;
                }

                // Create a Media record with the audio blob reference and timing metadata
                var chatId = ChatId.Parse(textEntry.ChatId);
                var mediaId = MediaId.New(chatId.Value);
                var metadata = PropertyBag.Empty
                    .Set(nameof(ChatEntryAudio.BeginsAt), audioDbEntry.BeginsAt.Ticks)
                    .Set(nameof(ChatEntryAudio.EndsAt),
                        audioDbEntry.EndsAt.HasValue ? audioDbEntry.EndsAt.Value.Ticks : (object?)null)
                    .Set(nameof(ChatEntryAudio.ContentEndsAt),
                        audioDbEntry.ContentEndsAt.HasValue ? audioDbEntry.ContentEndsAt.Value.Ticks : (object?)null)
                    .Set(nameof(ChatEntryAudio.ClientSideBeginsAt),
                        audioDbEntry.ClientSideBeginsAt.HasValue ? audioDbEntry.ClientSideBeginsAt.Value.Ticks : (object?)null);

                var media = new MediaFull(mediaId) {
                    BlobId = audioBlobId,
                    ContentType = "audio/webm",
                    Metadata = metadata,
                };
                var mediaChange = new MediaBackend_Change(mediaId, null, new Change<MediaFull> { Create = media });
                await Commander.Call(mediaChange, cancellationToken).ConfigureAwait(false);

                // Update the text entry to point to the new media
                var opDbContext = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
                await using var __ = opDbContext.ConfigureAwait(false);

                var dbTextEntry = await opDbContext.ChatEntries
                    .SingleAsync(e => e.Id == textEntry.Id, cancellationToken)
                    .ConfigureAwait(false);
                dbTextEntry.AudioId = mediaId.Value;
                await opDbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                totalMigrated++;
            }

            Log.LogInformation("Migrated {Count} audio entries so far (batch of {BatchSize})",
                totalMigrated, textEntries.Count);
        }

        Log.LogInformation("Audio entry migration complete. Total migrated: {Total}", totalMigrated);
    }

    // [CommandHandler]
    public virtual async Task OnFixCorruptedReadPositions(
        ChatsUpgradeBackend_FixCorruptedReadPositions command,
        CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return; // It just spawns other commands, so nothing to do here

        var chatPositionsBackend = Services.GetRequiredService<IChatPositionsBackend>();

        var userIds = await ListAllAccountIds(cancellationToken).ConfigureAwait(false);
        foreach (var userId in userIds) {
            var chatIds = await AuthorsUpgradeBackend.ListChatIds(userId, cancellationToken).ConfigureAwait(false);
            foreach (var chatId in chatIds) {
                var position = await chatPositionsBackend
                    .Get(userId, chatId, ChatPositionKind.Read, cancellationToken)
                    .ConfigureAwait(false);
                if (position.EntryLid <= 0)
                    continue;

                var idRange = await ChatsBackend.GetIdRange(chatId, false, cancellationToken).ConfigureAwait(false);
                var lastEntryLid = idRange.End - 1;
                if (lastEntryLid >= position.EntryLid)
                    continue;

                // since it was corrupted for some time and user might not know that there are some new message
                // let's show at least 1 unread message, so user could pay attention to this chat
                var fixedPosition = new ChatPosition(lastEntryLid - 1);
                Log.LogInformation(
                    "Fixing corrupted last read position for user #{UserId} at chat #{ChatId}: {CurrentPosition} -> {FixedPosition}",
                    userId,
                    chatId,
                    position,
                    fixedPosition);
                var setCmd = new ChatPositionsBackend_Set(userId, chatId, ChatPositionKind.Read, fixedPosition, true);
                await Commander.Call(setCmd, true, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<UserId[]> ListAllAccountIds(CancellationToken cancellationToken)
    {
        var result = new List<UserId>();
        UserId? lastId = null;
        while (true) {
            var batch = await AccountsBackend.ListChanged(0, long.MaxValue, lastId, 1000, cancellationToken)
                .ConfigureAwait(false);
            if (batch.Length == 0)
                break;
            result.AddRange(batch);
            lastId = batch[^1];
        }
        return result.ToArray();
    }
}
