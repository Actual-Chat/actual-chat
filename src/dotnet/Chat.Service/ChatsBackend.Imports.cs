using ActualChat.Chat.Db;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat;

public partial class ChatsBackend
{
    public virtual async Task<ChatImportSession?> GetImport(ChatId chatId, CancellationToken cancellationToken)
    {
        foreach (var key in chatId.ToMaintenanceKeyChain().Skip(1)) {
            var parent = await GetImport(ChatId.Parse(ContentRef.Parse(key.Value).ContentId.Value), cancellationToken)
                .ConfigureAwait(false);
            if (parent is { IsActive: true })
                return parent;
        }
        var db = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var lease = db.ConfigureAwait(false);
        var row = await db.ChatImports.AsNoTracking().SingleOrDefaultAsync(x => x.Id == chatId.Value, cancellationToken)
            .ConfigureAwait(false);
        return row?.ToModel();
    }

    public virtual async Task<ApiArray<UserId>> ListImportConsents(
        ChatId chatId, string importId, CancellationToken cancellationToken)
    {
        var import = await GetImport(chatId, cancellationToken).ConfigureAwait(false);
        if (import is not { IsActive: true } || import.Id != importId)
            return default;

        var db = await DbHub.CreateDbContext(cancellationToken).ConfigureAwait(false);
        await using var lease = db.ConfigureAwait(false);
        var ids = await db.ChatImportConsents.AsNoTracking()
            .Where(x => x.ImportId == importId && x.HasConsent).Select(x => x.UserId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return ids.Select(UserId.Parse).ToApiArray();
    }

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

    public virtual async Task<ChatImportSession> OnStartImport(
        ChatsBackend_StartImport command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive) {
            _ = GetImport(command.ChatId, default);
            return null!;
        }

        var chatId = command.ChatId;
        if (chatId is not GroupChatId and not PlaceChatId)
            throw StandardError.Constraint("Only group chats and Places support import.");

        var db = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var lease = db.ConfigureAwait(false);
        await ChatImportGuard.Lock(db, chatId, cancellationToken).ConfigureAwait(false);
        await RequireImportOwner(chatId, command.UserId, cancellationToken).ConfigureAwait(false);
        var chat = await Get(chatId, cancellationToken).Require().ConfigureAwait(false);
        if (chat.AllowAnonymousAuthors)
            throw StandardError.Constraint("Anonymous chats do not support import.");

        var row = await db.ChatImports.SingleOrDefaultAsync(x => x.Id == chatId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (row?.ImportId == command.ImportId)
            return row.ToModel();

        var scopeIds = chatId.ToMaintenanceKeyChain().Select(x => ContentRef.Parse(x.Value).ContentId.Value).ToArray();
        if (await db.ChatImports.AnyAsync(x => x.IsActive && scopeIds.Contains(x.Id), cancellationToken)
            .ConfigureAwait(false))
            throw StandardError.Constraint("An import is already active in this scope.");
        if (chatId is PlaceChatId { IsRoot: true } rootId) {
            var prefix = PlaceChatId.IdPrefix + rootId.PlaceId.Value + "-";
            if (await db.ChatImports.AnyAsync(x => x.IsActive && x.Id.StartsWith(prefix), cancellationToken)
                .ConfigureAwait(false))
                throw StandardError.Constraint("A chat in this Place is already importing.");
            foreach (var childId in await ListPlaceChatIds(rootId.PlaceId, cancellationToken).ConfigureAwait(false))
                await Services.GetRequiredService<IMaintenancesBackend>().RequireAvailable(childId, cancellationToken)
                    .ConfigureAwait(false);
        }

        await Services.GetRequiredService<IMaintenancesBackend>().RequireAvailable(chatId, cancellationToken)
            .ConfigureAwait(false);
        if (row is null) {
            row = new DbChatImport { Id = chatId.Value };
            db.Add(row);
        }
        row.ImportId = command.ImportId;
        row.StartedBy = command.UserId.Value;
        row.StartedAt = Clocks.SystemClock.Now;
        row.IsActive = true;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        CommandContext.GetCurrent().Operation.AddEvent(new ChatImportChangedEvent(chatId, row.ImportId));
        return row.ToModel();
    }

    public virtual async Task OnEndImport(ChatsBackend_EndImport command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive) {
            _ = GetImport(command.ChatId, default);
            _ = ListImportConsents(command.ChatId, command.ImportId, default);
            return;
        }

        var db = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var lease = db.ConfigureAwait(false);
        await ChatImportGuard.Lock(db, command.ChatId, cancellationToken).ConfigureAwait(false);
        await RequireImportOwner(command.ChatId, command.UserId, cancellationToken).ConfigureAwait(false);
        var row = await db.ChatImports.SingleAsync(x => x.Id == command.ChatId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (row.ImportId != command.ImportId)
            throw StandardError.Constraint("The import session has changed.");

        row.IsActive = false;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        CommandContext.GetCurrent().Operation.AddEvent(new ChatImportChangedEvent(command.ChatId, row.ImportId));
    }

    public virtual async Task OnSetImportConsent(
        ChatsBackend_SetImportConsent command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive) {
            _ = ListImportConsents(command.ChatId, command.ImportId, default);
            return;
        }

        var db = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var lease = db.ConfigureAwait(false);
        await ChatImportGuard.Lock(db, command.ChatId, cancellationToken).ConfigureAwait(false);
        await RequireActiveImport(db, command.ChatId, command.ImportId, cancellationToken).ConfigureAwait(false);
        await RequireImportAuthor(command.ChatId, command.UserId, cancellationToken).ConfigureAwait(false);
        var id = command.ImportId + ":" + command.UserId.Value;
        var row = await db.ChatImportConsents.FindAsync([id], cancellationToken).ConfigureAwait(false);
        if (row is null) {
            row = new DbChatImportConsent { Id = id, ImportId = command.ImportId, UserId = command.UserId.Value };
            db.Add(row);
        }
        row.HasConsent = command.HasConsent;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnRegisterImportUpload(
        ChatsBackend_RegisterImportUpload command, CancellationToken cancellationToken)
    {
        var upload = command.Upload;
        if (Invalidation.IsActive) {
            _ = GetImportUpload(upload.ChatId, upload.UploadId, default);
            return;
        }

        var db = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var lease = db.ConfigureAwait(false);
        await ChatImportGuard.Lock(db, upload.ChatId, cancellationToken).ConfigureAwait(false);
        var import = await RequireActiveImport(db, upload.ChatId, upload.ImportId, cancellationToken)
            .ConfigureAwait(false);
        await RequireImportOwner(ChatId.Parse(import.Id), upload.UploadedBy, cancellationToken).ConfigureAwait(false);
        await RequireImportAuthor(upload.ChatId, upload.UserId, cancellationToken).ConfigureAwait(false);
        if (!await db.ChatImportConsents.AnyAsync(x => x.ImportId == upload.ImportId
            && x.UserId == upload.UserId.Value && x.HasConsent, cancellationToken).ConfigureAwait(false))
            throw StandardError.Constraint("This member has not consented to import.");

        var row = await db.ChatImportUploads.FindAsync([upload.UploadId.Value], cancellationToken)
            .ConfigureAwait(false);
        if (row is null) {
            row = new DbChatImportUpload {
                Id = upload.UploadId.Value, ChatId = upload.ChatId.Value, ImportId = upload.ImportId,
                UserId = upload.UserId.Value, UploadedBy = upload.UploadedBy.Value,
            };
            db.Add(row);
        }
        if (row.ChatId != upload.ChatId.Value || row.ImportId != upload.ImportId
            || row.UserId != upload.UserId.Value || row.UploadedBy != upload.UploadedBy.Value || row.EntryId != null)
            throw StandardError.Constraint("The import upload cannot be changed.");

        if (upload.Media is { } mediaRef) {
            var media = await MediaBackend.GetFull(mediaRef.MediaId, cancellationToken).Require().ConfigureAwait(false);
            if (media.UserId != upload.UserId)
                throw StandardError.Constraint("The media owner does not match the imported author.");
            if (mediaRef.ThumbnailMediaId is { } thumbnailId) {
                var thumbnail = await MediaBackend.GetFull(thumbnailId, cancellationToken).Require()
                    .ConfigureAwait(false);
                if (thumbnail.UserId != upload.UserId)
                    throw StandardError.Constraint("The thumbnail owner does not match the imported author.");
            }
            row.MediaJson = JsonSerializer.Serialize(mediaRef);
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task OnImportChangedEvent(ChatImportChangedEvent command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var db = await DbHub.CreateOperationDbContext(cancellationToken).ConfigureAwait(false);
        await using var lease = db.ConfigureAwait(false);
        await ChatImportGuard.Lock(db, command.ChatId, cancellationToken).ConfigureAwait(false);
        var row = await db.ChatImports.FindAsync([command.ChatId.Value], cancellationToken).ConfigureAwait(false);
        if (row is null || row.ImportId != command.ImportId)
            return;

        if (!row.IsActive && await Services.GetRequiredService<IMaintenancesBackend>()
            .Get(command.ChatId.ToMaintenanceKey(), cancellationToken).ConfigureAwait(false) != MaintenanceMode.Import)
            return;

        var mode = row.IsActive ? MaintenanceMode.Import : MaintenanceMode.None;
        await Commander.Call(new MaintenancesBackend_Set(command.ChatId.ToMaintenanceKey(), mode) {
            OwnerId = row.ImportId,
        }, true, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<DbChatImport> RequireActiveImport(
        ChatDbContext db, ChatId chatId, string importId, CancellationToken cancellationToken)
    {
        var keys = chatId.ToMaintenanceKeyChain();
        var maintenances = Services.GetRequiredService<IMaintenancesBackend>();
        foreach (var key in keys) {
            var mode = await maintenances.Get(key, cancellationToken).ConfigureAwait(false);
            if (mode is not MaintenanceMode.None and not MaintenanceMode.Import)
                throw StandardError.Constraint("Another maintenance operation is active.");
        }
        var ids = keys.Select(x => ContentRef.Parse(x.Value).ContentId.Value).ToArray();
        return await db.ChatImports.SingleOrDefaultAsync(
            x => ids.Contains(x.Id) && x.ImportId == importId && x.IsActive, cancellationToken).ConfigureAwait(false)
            ?? throw StandardError.Constraint("The import session is not active.");
    }

    private async Task RequireImportOwner(ChatId chatId, UserId userId, CancellationToken cancellationToken)
    {
        var rules = await GetRules(chatId, userId, cancellationToken).ConfigureAwait(false);
        if (!rules.IsOwner())
            throw StandardError.Unauthorized("Only owners can manage chat imports.");
    }

    private async Task<AuthorFull> RequireImportAuthor(
        ChatId chatId, UserId userId, CancellationToken cancellationToken)
    {
        var rules = await GetRules(chatId, userId, cancellationToken).ConfigureAwait(false);
        var author = await AuthorsBackend.GetByUserId(chatId, userId, RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        if (!rules.CanRead() || author is null || author.HasLeft || author.IsAnonymous || userId.IsGuest)
            throw StandardError.Constraint("The imported author must be a current nonanonymous member.");

        return author;
    }
}
