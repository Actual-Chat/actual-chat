using ActualChat.Chat.Db;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public partial class ChatsBackend
{
    public virtual async Task<ApiArray<ChatImportEntryResult>> OnImportEntries(
        ChatsBackend_ImportEntries command, CancellationToken cancellationToken)
    {
        var chatId = command.ChatId;
        var context = CommandContext.GetCurrent();
        if (Invalidation.IsActive) {
            foreach (var id in context.Operation.Items.Get<ChatEntryId[]>("ImportedEntryIds") ?? []) {
                InvalidateTiles(chatId, id.LocalId, ChangeKind.Create, false);
                _ = GetEntryAttachments(id, default);
            }
            _ = GetMinLid(chatId, default);
            _ = GetMaxLid(chatId, true, default);
            _ = GetMaxLid(chatId, false, default);
            return default;
        }

        if (chatId is not GroupChatId and not PlaceChatId || chatId is PlaceChatId { IsRoot: true })
            throw StandardError.Constraint("Import requires a group chat timeline.");
        if (command.Entries.Count is < 1 or > 100)
            throw StandardError.Constraint("An import batch must contain 1 to 100 messages.");
        if (command.Entries.Any(x => x is null || x.UserId is null || x.Content is null))
            throw StandardError.Constraint("Invalid import entry.");

        command.BatchId.RequireMaxLength(100);
        var db = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var lease = db.ConfigureAwait(false);
        await ChatImportGuard.Lock(db, chatId, cancellationToken).ConfigureAwait(false);
        var import = await RequireActiveImport(db, chatId, command.ImportId, cancellationToken).ConfigureAwait(false);
        await RequireImportOwner(ChatId.Parse(import.Id), command.UserId, cancellationToken).ConfigureAwait(false);
        var callerRules = await GetRules(chatId, command.UserId, cancellationToken).ConfigureAwait(false);
        callerRules.Permissions.Require(ChatPermissions.Read);

        var batchId = command.ImportId + ":" + chatId.Value + ":" + command.BatchId;
        var request = JsonSerializer.Serialize(command.Entries);
        var receipt = await db.ChatImportBatches.FindAsync([batchId], cancellationToken).ConfigureAwait(false);
        if (receipt is not null) {
            if (receipt.Request != request)
                throw StandardError.Constraint("The import batch ID was already used with different entries.");

            return JsonSerializer.Deserialize<ApiArray<ChatImportEntryResult>>(receipt.Result);
        }

        var tail = await db.ChatEntries
            .Where(x => x.ChatId == chatId.Value && x.Kind == 0 && !x.IsThreadEntry && !x.IsRemoved)
            .OrderByDescending(x => x.LocalId).Select(x => (DateTime?)x.BeginsAt)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var consenting = (await db.ChatImportConsents.Where(x => x.ImportId == command.ImportId && x.HasConsent)
            .Select(x => x.UserId).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();
        var results = new ChatImportEntryResult[command.Entries.Count];
        var createdIds = new List<ChatEntryId>();
        foreach (var (input, index) in command.Entries.Select((entry, index) => (entry, index))
            .OrderBy(x => x.entry.BeginsAt)) {
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
                    EntryId = id, Index = i, Version = VersionGenerator.NextVersion(),
                    MediaId = media.MediaId, ThumbnailMediaId = media.ThumbnailMediaId,
                };
            }).ToArray();
            ChatEntry entry = new TextEntry(id, VersionGenerator.NextVersion()) {
                AuthorId = author!.Id, BeginsAt = input.BeginsAt, Content = input.Content,
                RepliedEntryLid = input.RepliedEntryLid, Attachments = attachments, IsImported = true,
            };
            entry = await PrepareTextEntryForSave(entry, null, cancellationToken).ConfigureAwait(false);
            db.Add(new DbChatEntry(entry) { HasAttachments = attachments.Length > 0 });
            foreach (var attachment in attachments)
                db.Add(new DbChatEntryAttachment(attachment));
            foreach (var upload in uploads)
                upload.EntryId = id.Value;
            context.Operation.AddEvent(new ChatEntryChangedEvent(entry, author, ChangeKind.Create, null) {
                SuppressNotifications = true,
            });
            createdIds.Add(id);
            results[index] = new ChatImportEntryResult(id, ExceptionInfo.None);
            tail = input.BeginsAt;
        }
        db.Add(new DbChatImportBatch { Id = batchId, Request = request, Result = JsonSerializer.Serialize(results) });
        context.Operation.Items.Set("ImportedEntryIds", createdIds.ToArray());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return results.ToApiArray();

        async Task<(AuthorFull? Author, List<DbChatImportUpload> Uploads, string? Error)> Validate(
            ChatImportEntry input) {
            DateTime date = input.BeginsAt;
            if (date.Ticks % 10 != 0)
                return (null, [], "Import timestamps must have microsecond precision.");
            if (tail is { } last && date <= last)
                return (null, [], "The imported timestamp must be strictly later than the last accepted entry.");
            if (!consenting.Contains(input.UserId.Value))
                return (null, [], "This member has not consented to import.");
            if (input.Content.Length > Constants.Chat.MaxEntryTextLength)
                return (null, [], "The imported message is too long.");
            if (input.UploadIds.Count > Constants.Attachments.FileCountLimit)
                return (null, [], "Too many attachments.");
            if (input.Content.IsNullOrWhiteSpace() && input.UploadIds.Count == 0)
                return (null, [], "Cannot import an empty message.");

            var author = await AuthorsBackend.GetByUserId(chatId, input.UserId,
                RequestedAuthorKind.Full, cancellationToken).ConfigureAwait(false);
            var authorRules = await GetRules(chatId, input.UserId, cancellationToken).ConfigureAwait(false);
            if (!authorRules.CanRead() || author is null || author.HasLeft
                || author.IsAnonymous || input.UserId.IsGuest)
                return (null, [], "The imported author must be a current nonanonymous member.");
            if (input.RepliedEntryLid is { } reply && !await db.ChatEntries.AnyAsync(
                x => x.ChatId == chatId.Value && x.LocalId == reply && !x.IsRemoved, cancellationToken)
                .ConfigureAwait(false))
                return (null, [], "The reply target does not exist in this chat.");

            var uploads = new List<DbChatImportUpload>();
            foreach (var uploadId in input.UploadIds) {
                var upload = await db.ChatImportUploads.FindAsync([uploadId.Value], cancellationToken)
                    .ConfigureAwait(false);
                if (upload is null || upload.ImportId != import.ImportId || upload.ChatId != chatId.Value
                    || upload.UserId != input.UserId.Value || upload.EntryId != null || upload.MediaJson.IsNullOrEmpty()
                    || uploads.Contains(upload))
                    return (null, [], "The attachment is not available for this author and import session.");

                uploads.Add(upload);
            }
            return (author, uploads, null);
        }
    }
}
