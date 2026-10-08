using ActualChat.Chat.Module;

namespace ActualChat.Chat;

/// <summary>
/// Owner-facing import sessions: an import is an <see cref="MaintenanceMode.Import"/> maintenance
/// owned by the session's ID, so starting and ending one writes nothing to the Chat database.
/// </summary>
public class ChatImports(IServiceProvider services) : IChatImports
{
    private ChatSettings Settings => field ??= services.GetRequiredService<ChatSettings>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IAuthorsBackend Authors { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private IMaintenancesBackend MaintenancesBackend { get; } = services.GetRequiredService<IMaintenancesBackend>();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private ICommander Commander { get; } = services.Commander();

    // [ComputeMethod]
    public virtual async Task<ChatImportSession?> Get(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Permissions.Require(ChatPermissions.Read);

        return await ChatsBackend.GetImport(chatId, cancellationToken).ConfigureAwait(false);
    }

    // [ComputeMethod]
    public virtual async Task<bool> HasConsent(
        Session session, ChatId chatId, ChatImportId importId, CancellationToken cancellationToken)
    {
        var import = await Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (import?.ImportId != importId)
            return false;

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var consents = await ChatsBackend
            .ListImportConsents(import.ChatId, importId, cancellationToken)
            .ConfigureAwait(false);
        return consents.Contains(account.Id);
    }

    // [ComputeMethod]
    public virtual async Task<ChatImportConsentSummary> GetConsentSummary(
        Session session, ChatId chatId, int offset, int limit, CancellationToken cancellationToken)
    {
        if (offset < 0 || limit is < 1 or > 100)
            throw StandardError.Constraint("Invalid member page.");

        // Every reader sees the counts; only owners see who hasn't consented yet
        var import = await Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        var scope = await Chats.Get(session, import.ChatId, cancellationToken).ConfigureAwait(false);
        var isOwner = scope?.Rules.IsOwner() == true;

        var members = await Authors.ListUserIds(import.ChatId, cancellationToken).ConfigureAwait(false);
        var consents = await ChatsBackend
            .ListImportConsents(import.ChatId, import.ImportId, cancellationToken)
            .ConfigureAwait(false);
        var consentSet = consents.ToHashSet();
        var memberIds = members.Where(x => !x.IsGuest).Distinct().OrderBy(x => x.Value).ToArray();
        var pending = memberIds.Where(x => !consentSet.Contains(x)).ToArray();
        return new ChatImportConsentSummary(
            ConsentingCount: memberIds.Length - pending.Length,
            NonConsentingCount: pending.Length,
            NonConsentingUserIds: isOwner ? pending.Skip(offset).Take(limit).ToApiArray() : default);
    }

    // [CommandHandler]
    public virtual async Task<ChatImportSession> OnStart(
        ChatImports_Start command, CancellationToken cancellationToken)
    {
        var chatId = command.ChatId;
        if (chatId is not GroupChatId and not PlaceChatId)
            throw StandardError.Constraint("Only group chats and Places support import.");

        var userId = await RequireOwner(command.Session, chatId, cancellationToken).ConfigureAwait(false);
        var chat = await Chats.Get(command.Session, chatId, cancellationToken).Require().ConfigureAwait(false);
        if (chat.AllowAnonymousAuthors)
            throw StandardError.Constraint("Anonymous chats do not support import.");

        var importId = ChatImportId.TryParse(ChatImportId.Format(chatId, command.Uuid))
            ?? throw StandardError.Constraint("Invalid command ID.");
        // Every import in a Place writes the root's row, so it holds whatever runs anywhere in the Place
        var key = chatId.ToMaintenanceKey();
        var current = await MaintenancesBackend.Get(key, cancellationToken).ConfigureAwait(false);
        if (current.Mode == MaintenanceMode.Import && current.OwnerId == importId.Value)
            return new ChatImportSession(importId) { // A retry of this very command
                StartedBy = current.StartedBy,
                StartedAt = current.StartedAt,
            };

        current.Mode.RequireNone(chatId is PlaceChatId ? "Place" : "chat");
        var hasImportRecords = await ChatsBackend
            .HasImportRecords(chatId, importId, cancellationToken)
            .ConfigureAwait(false);
        if (hasImportRecords)
            throw StandardError.Constraint("This import session has already ended.");

        var import = new ChatImportSession(importId) {
            StartedBy = userId,
            StartedAt = Clocks.SystemClock.Now,
        };
        // The first Start wins: OnSet refuses a row another import already owns
        var setMaintenanceCmd = new MaintenancesBackend_Set(key, MaintenanceMode.Import) {
            OwnerId = importId.Value,
            StartedBy = import.StartedBy,
            StartedAt = import.StartedAt,
            TargetDiff = chatId is PlaceChatId { IsRoot: false }
                ? new([chatId.ToMaintenanceTarget()!])
                : default,
        };
        await Commander.Call(setMaintenanceCmd, cancellationToken).ConfigureAwait(false);

        await RequireNothingPostedSince(import, cancellationToken).ConfigureAwait(false);
        return import;
    }

    // [CommandHandler]
    public virtual async Task OnEnd(ChatImports_End command, CancellationToken cancellationToken)
    {
        var import = await ChatsBackend.GetImport(command.ChatId, cancellationToken).Require().ConfigureAwait(false);
        if (import.ImportId != command.ImportId)
            throw StandardError.Constraint("The import session has changed.");

        await RequireOwner(command.Session, import.ChatId, cancellationToken).ConfigureAwait(false);

        var setMaintenanceCmd = new MaintenancesBackend_Set(import.ChatId.ToMaintenanceKey(), MaintenanceMode.None) {
            OwnerId = import.ImportId.Value,
        };
        await Commander.Call(setMaintenanceCmd, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task OnSetConsent(ChatImports_SetConsent command, CancellationToken cancellationToken)
    {
        var import = await Get(command.Session, command.ChatId, cancellationToken).Require().ConfigureAwait(false);
        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        var setImportConsentCmd = new ChatsBackend_SetImportConsent(
            import.ChatId, account.Id, command.ImportId, command.HasConsent);
        await Commander.Call(setImportConsentCmd, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<ApiArray<ChatImportEntryResult>> OnImportEntries(
        ChatImports_ImportEntries command, CancellationToken cancellationToken)
    {
        var import = await Get(command.Session, command.ChatId, cancellationToken).Require().ConfigureAwait(false);
        var userId = await RequireOwner(command.Session, import.ChatId, cancellationToken).ConfigureAwait(false);

        var importEntriesCmd = new ChatsBackend_ImportEntries(
            command.ChatId, userId, command.ImportId, command.Uuid, command.Entries);
        return await Commander.Call(importEntriesCmd, cancellationToken).ConfigureAwait(false);
    }

    // [CommandHandler]
    public virtual async Task<UploadId> OnCreateUpload(
        ChatImports_CreateUpload command, CancellationToken cancellationToken)
    {
        var import = await Get(command.Session, command.ChatId, cancellationToken).Require().ConfigureAwait(false);
        var userId = await RequireOwner(command.Session, import.ChatId, cancellationToken).ConfigureAwait(false);
        if (import.ImportId != command.ImportId)
            throw StandardError.Constraint("The import session is not active.");

        var consents = await ChatsBackend
            .ListImportConsents(import.ChatId, import.ImportId, cancellationToken)
            .ConfigureAwait(false);
        if (!consents.Contains(command.UserId))
            throw StandardError.Constraint("This member has not consented to import.");

        var metadata = new Upload(UploadId.New(), userId, command.Length, command.ChatId.Value, default) {
                FileName = command.FileName,
                ContentType = command.ContentType,
            }.Metadata;
        var createUploadCmd = new Uploads_Create {
            Session = command.Session, Length = command.Length,
            Tag = UploadExt.BuildTag(command.ChatId), Metadata = metadata,
        };
        var uploadId = await Commander.Call(createUploadCmd, cancellationToken).ConfigureAwait(false);

        var registerImportUploadCmd = new ChatsBackend_RegisterImportUpload(
            new ChatImportUpload(command.ChatId, import.ImportId, uploadId, command.UserId, userId, null));
        await Commander.Call(registerImportUploadCmd, cancellationToken).ConfigureAwait(false);

        return uploadId;
    }

    // [CommandHandler]
    public virtual async Task<MediaRef> OnFinalizeUpload(
        ChatImports_FinalizeUpload command, CancellationToken cancellationToken)
    {
        var import = await Get(command.Session, command.ChatId, cancellationToken).Require().ConfigureAwait(false);
        var userId = await RequireOwner(command.Session, import.ChatId, cancellationToken).ConfigureAwait(false);
        var upload = await ChatsBackend
            .GetImportUpload(command.ChatId, command.UploadId, cancellationToken)
            .Require().ConfigureAwait(false);
        if (import.ImportId != command.ImportId || upload.ImportId != import.ImportId || upload.UploadedBy != userId)
            throw StandardError.Constraint("The import upload is not available.");

        var registerImportUploadCmd1 = new ChatsBackend_RegisterImportUpload(upload);
        await Commander.Call(registerImportUploadCmd1, cancellationToken).ConfigureAwait(false);

        var convertToMediaRefCmd = new Uploads_ConvertToMediaRef {
            Session = command.Session,
            UploadId = upload.UploadId,
        };
        var mediaRef = await Commander.Call(convertToMediaRefCmd, cancellationToken).ConfigureAwait(false);

        var registerImportUploadCmd2 = new ChatsBackend_RegisterImportUpload(upload with { Media = mediaRef });
        await Commander.Call(registerImportUploadCmd2, cancellationToken).ConfigureAwait(false);
        return mediaRef;
    }

    // Private methods

    private async ValueTask<UserId> RequireOwner(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        if (!chat.Rules.IsOwner())
            throw StandardError.Unauthorized("Only owners can manage chat imports.");

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeActive);
        return account.Id;
    }

    private async Task RequireNothingPostedSince(ChatImportSession import, CancellationToken cancellationToken)
    {
        // Waits until every open stream in the scope has seen the import and stopped, then checks
        // nothing was posted meanwhile: such a message would sort after all imported history.
        await Task.Delay(Settings.ImportStartSettleDelay, cancellationToken).ConfigureAwait(false);

        ChatId[] chatIds = import.ChatId is PlaceChatId { IsRoot: true } rootChatId
            ? await ChatsBackend.ListPlaceChatIds(rootChatId.PlaceId, cancellationToken).ConfigureAwait(false)
            : [import.ChatId];
        foreach (var chatId in chatIds) {
            var entryId = await ChatsBackend
                .FindEntryPostedSince(chatId, import.StartedAt, cancellationToken)
                .ConfigureAwait(false);
            if (entryId is null)
                continue;

            var key = import.ChatId.ToMaintenanceKey();
            var clearMaintenanceCmd = new MaintenancesBackend_Set(key, MaintenanceMode.None) {
                OwnerId = import.ImportId.Value,
            };
            await Commander.Call(clearMaintenanceCmd, CancellationToken.None).ConfigureAwait(false);
            throw StandardError.Constraint(
                $"Message #{entryId.LocalId} was posted while the import was starting. "
                + "Remove it, then start the import again.");
        }
    }
}
