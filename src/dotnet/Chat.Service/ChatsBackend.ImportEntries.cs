using ActualChat.Chat.Db;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public partial class ChatsBackend
{
    // [CommandHandler]
    public virtual async Task<ApiArray<ChatImportEntryResult>> OnImportEntries(
        ChatsBackend_ImportEntries command, CancellationToken cancellationToken)
    {
        var chatId = command.ChatId;
        var context = CommandContext.GetCurrent();
        if (chatId is not GroupChatId and not PlaceChatId or PlaceChatId { IsRoot: true })
            throw StandardError.Constraint("Import requires a group chat timeline.");
        if (command.Entries.Count is < 1 or > 100)
            throw StandardError.Constraint("An import batch must contain 1 to 100 messages.");
        if (command.Entries.Any(x => ReferenceEquals(x?.UserId, null) || ReferenceEquals(x.Content, null)))
            throw StandardError.Constraint("Invalid import entry.");

        command.BatchId.RequireMaxLength(100);
        var import = await RequireActiveImport(chatId, command.ImportId, cancellationToken).ConfigureAwait(false);

        var db = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var lease = db.ConfigureAwait(false);
        await LockImportScope(db, chatId, cancellationToken).ConfigureAwait(false);
        await RequireImportOwner(import.ChatId, command.UserId, cancellationToken).ConfigureAwait(false);
        var callerRules = await GetRules(chatId, command.UserId, cancellationToken).ConfigureAwait(false);
        callerRules.Permissions.Require(ChatPermissions.Read);

        var batchId = command.ImportId.Value + ":" + chatId.Value + ":" + command.BatchId;
        var request = JsonSerializer.Serialize(command.Entries);
        var receipt = await db.ChatImportBatches.FindAsync([batchId], cancellationToken).ConfigureAwait(false);
        if (receipt is not null) {
            if (receipt.Request != request)
                throw StandardError.Constraint("The import batch ID was already used with different entries.");

            return JsonSerializer.Deserialize<ApiArray<ChatImportEntryResult>>(receipt.Result);
        }

        var tailEntry = await db.ChatEntries
            .Where(x => x.ChatId == chatId.Value && x.Kind == 0 && !x.IsThreadEntry && !x.IsRemoved)
            .OrderByDescending(x => x.LocalId)
            .Select(x => new { x.LocalId, x.BeginsAt })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var tail = (DateTime?)tailEntry?.BeginsAt;
        // Imported history must stay behind everything posted after the import ends
        var now = (DateTime)Clocks.SystemClock.Now;
        var consents = await db.ChatImportConsents
            .Where(x => x.ImportId == command.ImportId.Value && x.HasConsent)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var consentSet = consents.ToHashSet();

        var results = new ChatImportEntryResult[command.Entries.Count];
        var createdIds = new List<ChatEntryId>();
        var authorIds = new HashSet<AuthorId>();
        var orderedEntries = command.Entries
            .Select((entry, index) => (Entry: entry, Index: index))
            .OrderBy(x => x.Entry.BeginsAt);
        foreach (var (input, index) in orderedEntries) {
            var (author, uploads, error) = await Validate(input).ConfigureAwait(false);
            if (error is not null) {
                results[index] = new ChatImportEntryResult(null, new ExceptionInfo(StandardError.Constraint(error)));
                continue;
            }

            var localId = await DbNextLocalId(db, chatId, cancellationToken).ConfigureAwait(false);
            var id = ChatEntryId.New(chatId, localId);
            var attachments = uploads.Select((upload, i) => {
                var media = upload.ToModel().Media!;
                return new ChatEntryAttachment {
                    EntryId = id,
                    Index = i,
                    Version = VersionGenerator.NextVersion(),
                    MediaId = media.MediaId,
                    ThumbnailMediaId = media.ThumbnailMediaId,
                };
            }).ToArray();
            ChatEntry entry = new TextEntry(id, VersionGenerator.NextVersion()) {
                AuthorId = author!.Id,
                BeginsAt = input.BeginsAt,
                Content = input.Content,
                RepliedEntryLid = input.RepliedEntryLid,
                Attachments = attachments,
                IsImported = true,
            };
            entry = await PrepareTextEntryForSave(entry, null, cancellationToken).ConfigureAwait(false);

            db.Add(new DbChatEntry(entry) { HasAttachments = attachments.Length > 0 });
            foreach (var attachment in attachments)
                db.Add(new DbChatEntryAttachment(attachment));
            foreach (var upload in uploads)
                upload.EntryId = id.Value;
            context.Operation.AddEvent(new ChatEntryChangedEvent(entry, author, ChangeKind.Create, null) {
                MustSkipNotification = true,
            });
            createdIds.Add(id);
            authorIds.Add(author.Id);
            results[index] = new ChatImportEntryResult(id, ExceptionInfo.None);
            tail = input.BeginsAt;
        }

        db.Add(new DbChatImportBatch {
            Id = batchId,
            Request = request,
            Result = JsonSerializer.Serialize(results),
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (createdIds.Count > 0) {
            var entryIds = createdIds.ToArray();
            var previousLid = tailEntry?.LocalId ?? 0L;
            Invalidation.Defer(() => {
                foreach (var id in entryIds) {
                    InvalidateTiles(chatId, id.LocalId, ChangeKind.Create, false);
                    _ = GetEntryAttachments(id, default);
                }
                // The batch appends past the existing tail, so only that entry's range tile can also
                // have changed - and only when it isn't in the same conversation tile as the first
                // imported entry, which InvalidateTiles already covered.
                if (previousLid != 0 && !ConversationIdTiles.GetTile(entryIds[0].LocalId).Range.Contains(previousLid))
                    _ = GetEntryRangeTile(chatId, ConversationIdTiles.GetTile(previousLid).Range.Start, default);
                _ = GetMinLid(chatId, default);
                _ = GetMaxLid(chatId, true, default);
                _ = GetMaxLid(chatId, false, default);
            });
        }

        foreach (var authorId in authorIds)
            await EnsurePlaceChatAuthorExists(authorId, command.ImportId, cancellationToken).ConfigureAwait(false);
        return results.ToApiArray();

        async ValueTask<(AuthorFull? Author, List<DbChatImportUpload> Uploads, string? Error)> Validate(
            ChatImportEntry input) {
            DateTime date = input.BeginsAt;
            if (date.Ticks % 10 != 0)
                return (null, [], "Import timestamps must have microsecond precision.");
            if (date >= now)
                return (null, [], "The imported timestamp must be in the past.");
            if (tail is { } last && date <= last)
                return (null, [], "The imported timestamp must be strictly later than the last accepted entry.");
            if (!consentSet.Contains(input.UserId.Value))
                return (null, [], "This member has not consented to import.");
            if (input.Content.Length > Constants.Chat.MaxEntryTextLength)
                return (null, [], "The imported message is too long.");
            if (input.UploadIds.Count > Constants.Attachments.FileCountLimit)
                return (null, [], "Too many attachments.");
            if (input.Content.IsNullOrWhiteSpace() && input.UploadIds.Count == 0)
                return (null, [], "Cannot import an empty message.");

            var author = await AuthorsBackend
                .GetByUserId(chatId, input.UserId, RequestedAuthorKind.Full, cancellationToken)
                .ConfigureAwait(false);
            var authorRules = await GetRules(chatId, input.UserId, cancellationToken).ConfigureAwait(false);
            if (!authorRules.CanRead() || author is null || author.HasLeft
                || author.IsAnonymous || input.UserId.IsGuest)
                return (null, [], "The imported author must be a current nonanonymous member.");

            if (input.RepliedEntryLid is { } repliedEntryLid) {
                var hasRepliedEntry = await db.ChatEntries
                    .AnyAsync(x => x.ChatId == chatId.Value && x.LocalId == repliedEntryLid && !x.IsRemoved,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!hasRepliedEntry)
                    return (null, [], "The reply target does not exist in this chat.");
            }

            var uploads = new List<DbChatImportUpload>();
            foreach (var uploadId in input.UploadIds) {
                var upload = await db.ChatImportUploads
                    .FindAsync([uploadId.Value], cancellationToken)
                    .ConfigureAwait(false);
                if (upload is null
                    || upload.ImportId != import.ImportId.Value
                    || upload.ChatId != chatId.Value
                    || upload.UserId != input.UserId.Value
                    || upload.EntryId != null
                    || upload.MediaJson.IsNullOrEmpty()
                    || uploads.Contains(upload))
                    return (null, [], "The attachment is not available for this author and import session.");

                uploads.Add(upload);
            }
            return (author, uploads, null);
        }
    }
}
