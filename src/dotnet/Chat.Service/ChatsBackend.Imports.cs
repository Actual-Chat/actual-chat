using ActualChat.Chat.Db;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public partial class ChatsBackend
{
    // [ComputeMethod]
    public virtual async Task<ChatImportSession?> GetImport(ChatId chatId, CancellationToken cancellationToken)
    {
        var maintenance = await MaintenancesBackend.GetImport(chatId, cancellationToken).ConfigureAwait(false);
        return maintenance is null
            ? null
            : new ChatImportSession(ChatImportId.Parse(maintenance.OwnerId)) {
                StartedBy = maintenance.StartedBy,
                StartedAt = maintenance.StartedAt,
            };
    }

    // [ComputeMethod]
    public virtual async Task<ApiArray<UserId>> ListImportConsents(
        ChatId chatId, ChatImportId importId, CancellationToken cancellationToken)
    {
        var import = await GetImport(chatId, cancellationToken).ConfigureAwait(false);
        if (import?.ImportId != importId)
            return default;

        var db = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var lease = db.ConfigureAwait(false);
        var userIds = await db.ChatImportConsents.AsNoTracking()
            .Where(x => x.ImportId == importId.Value && x.HasConsent)
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return userIds.Select(UserId.Parse).ToApiArray();
    }

    // [ComputeMethod]
    public virtual async Task<ChatImportUpload?> GetImportUpload(
        ChatId chatId, UploadId uploadId, CancellationToken cancellationToken)
    {
        var db = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var lease = db.ConfigureAwait(false);
        var upload = await db.ChatImportUploads.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == uploadId.Value && x.ChatId == chatId.Value, cancellationToken)
            .ConfigureAwait(false);
        return upload?.ToModel();
    }

    public virtual async Task<bool> HasImportRecords(
        ChatId chatId, ChatImportId importId, CancellationToken cancellationToken)
    {
        var db = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var lease = db.ConfigureAwait(false);
        var hasConsents = await db.ChatImportConsents
            .AnyAsync(x => x.ImportId == importId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (hasConsents)
            return true;

        var batchIdPrefix = importId.Value + ":";
        return await db.ChatImportBatches
            .AnyAsync(x => x.Id.StartsWith(batchIdPrefix), cancellationToken)
            .ConfigureAwait(false);
    }

    public virtual async Task<ChatEntryId?> FindEntryPostedSince(
        ChatId chatId, Moment since, CancellationToken cancellationToken)
    {
        var db = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var lease = db.ConfigureAwait(false);
        var sinceTime = since.ToDateTime();
        var entryId = await db.ChatEntries
            .Where(x => x.ChatId == chatId.Value && x.BeginsAt >= sinceTime && !x.IsRemoved)
            .OrderBy(x => x.LocalId)
            .Select(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return entryId is null ? null : ChatEntryId.Parse(entryId);
    }

    // [CommandHandler]
    public virtual async Task OnSetImportConsent(
        ChatsBackend_SetImportConsent command, CancellationToken cancellationToken)
    {
        var (chatId, userId, importId, hasConsent) = command;
        await RequireActiveImport(chatId, importId, cancellationToken).ConfigureAwait(false);

        var db = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var lease = db.ConfigureAwait(false);
        await LockImportScope(db, chatId, cancellationToken).ConfigureAwait(false);
        await RequireImportAuthor(chatId, userId, cancellationToken).ConfigureAwait(false);

        var id = importId.Value + ":" + userId.Value;
        var row = await db.ChatImportConsents.FindAsync([id], cancellationToken).ConfigureAwait(false);
        // Consent is final for the session: the importer may already rely on it
        if (row?.HasConsent == true && !hasConsent)
            throw StandardError.Constraint("Consent can't be revoked during an import.");

        if (row is null) {
            row = new DbChatImportConsent {
                Id = id,
                ImportId = importId.Value,
                UserId = userId.Value,
            };
            db.Add(row);
        }
        row.HasConsent = hasConsent;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Invalidation.Defer(() => {
            _ = ListImportConsents(chatId, importId, default);
        });
    }

    // [CommandHandler]
    public virtual async Task OnRegisterImportUpload(
        ChatsBackend_RegisterImportUpload command, CancellationToken cancellationToken)
    {
        var upload = command.Upload;
        var import = await RequireActiveImport(upload.ChatId, upload.ImportId, cancellationToken)
            .ConfigureAwait(false);

        var db = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var lease = db.ConfigureAwait(false);
        await LockImportScope(db, upload.ChatId, cancellationToken).ConfigureAwait(false);
        await RequireImportOwner(import.ChatId, upload.UploadedBy, cancellationToken).ConfigureAwait(false);
        await RequireImportAuthor(upload.ChatId, upload.UserId, cancellationToken).ConfigureAwait(false);

        var hasConsent = await db.ChatImportConsents
            .Where(x => x.ImportId == upload.ImportId.Value && x.UserId == upload.UserId.Value)
            .AnyAsync(x => x.HasConsent, cancellationToken)
            .ConfigureAwait(false);
        if (!hasConsent)
            throw StandardError.Constraint("This member has not consented to import.");

        var row = await db.ChatImportUploads
            .FindAsync([upload.UploadId.Value], cancellationToken)
            .ConfigureAwait(false);
        if (row is null) {
            row = new DbChatImportUpload {
                Id = upload.UploadId.Value,
                ChatId = upload.ChatId.Value,
                ImportId = upload.ImportId.Value,
                UserId = upload.UserId.Value,
                UploadedBy = upload.UploadedBy.Value,
            };
            db.Add(row);
        }
        if (row.ChatId != upload.ChatId.Value
            || row.ImportId != upload.ImportId.Value
            || row.UserId != upload.UserId.Value
            || row.UploadedBy != upload.UploadedBy.Value
            || row.EntryId != null)
            throw StandardError.Constraint("The import upload cannot be changed.");

        if (upload.Media is { } mediaRef) {
            var media = await MediaBackend
                .GetFull(mediaRef.MediaId, cancellationToken)
                .Require().ConfigureAwait(false);
            if (media.UserId != upload.UserId)
                throw StandardError.Constraint("The media owner does not match the imported author.");

            if (mediaRef.ThumbnailMediaId is { } thumbnailId) {
                var thumbnail = await MediaBackend
                    .GetFull(thumbnailId, cancellationToken)
                    .Require().ConfigureAwait(false);
                if (thumbnail.UserId != upload.UserId)
                    throw StandardError.Constraint("The thumbnail owner does not match the imported author.");
            }
            row.MediaJson = JsonSerializer.Serialize(mediaRef);
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var (uploadChatId, uploadId) = (upload.ChatId, upload.UploadId);
        Invalidation.Defer(() => {
            _ = GetImportUpload(uploadChatId, uploadId, default);
        });
    }

    // Private methods

    // Serializes an import session's own Chat DB writes - batches against consent changes - on the
    // key the whole scope shares. Ordinary chat writes don't take it: they are kept out of an import
    // by the maintenance check instead, so they must not contend with each other here.
    private static Task LockImportScope(ChatDbContext db, ChatId chatId, CancellationToken cancellationToken)
        => db.ChatImportConsents.Lock(chatId.ToMaintenanceKey().Value, cancellationToken);

    private async ValueTask<ChatImportSession> RequireActiveImport(
        ChatId chatId, ChatImportId importId, CancellationToken cancellationToken)
    {
        var maintenanceMode = await MaintenancesBackend.GetMode(chatId, cancellationToken).ConfigureAwait(false);
        if (maintenanceMode is not MaintenanceMode.None and not MaintenanceMode.Import)
            throw StandardError.Constraint("Another maintenance operation is active.");

        var import = await GetImport(chatId, cancellationToken).ConfigureAwait(false);
        if (import is null || import.ImportId != importId)
            throw StandardError.Constraint("The import session is not active.");

        return import;
    }

    private async ValueTask RequireImportOwner(ChatId chatId, UserId userId, CancellationToken cancellationToken)
    {
        var rules = await GetRules(chatId, userId, cancellationToken).ConfigureAwait(false);
        if (!rules.IsOwner())
            throw StandardError.Unauthorized("Only owners can manage chat imports.");
    }

    private async ValueTask RequireImportAuthor(ChatId chatId, UserId userId, CancellationToken cancellationToken)
    {
        var rules = await GetRules(chatId, userId, cancellationToken).ConfigureAwait(false);
        var author = await AuthorsBackend
            .GetByUserId(chatId, userId, RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        if (!rules.CanRead() || author is null || author.HasLeft || author.IsAnonymous || userId.IsGuest)
            throw StandardError.Constraint("The imported author must be a current nonanonymous member.");
    }
}
