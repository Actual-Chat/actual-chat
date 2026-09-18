namespace ActualChat.Chat;

public class ChatImports(IServiceProvider services) : IChatImports
{
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IChatsBackend Backend { get; } = services.GetRequiredService<IChatsBackend>();
    private IAuthorsBackend Authors { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private ICommander Commander { get; } = services.Commander();

    public virtual async Task<ChatImportSession?> Get(
        Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        chat.Rules.Permissions.Require(ChatPermissions.Read);
        return await Backend.GetImport(chatId, cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<bool> HasConsent(
        Session session, ChatId chatId, string importId, CancellationToken cancellationToken)
    {
        var import = await Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (import is not { IsActive: true } || import.Id != importId)
            return false;

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var consents = await Backend.ListImportConsents(import.ChatId, importId, cancellationToken)
            .ConfigureAwait(false);
        return consents.Contains(account.Id);
    }

    public virtual async Task<ChatImportConsentSummary> GetConsentSummary(
        Session session, ChatId chatId, int offset, int limit, CancellationToken cancellationToken)
    {
        if (offset < 0 || limit is < 1 or > 100)
            throw StandardError.Constraint("Invalid member page.");

        var import = await Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        await RequireOwner(session, import.ChatId, cancellationToken).ConfigureAwait(false);
        var members = await Authors.ListUserIds(import.ChatId, cancellationToken).ConfigureAwait(false);
        var consents = (await Backend.ListImportConsents(import.ChatId, import.Id, cancellationToken)
            .ConfigureAwait(false)).ToHashSet();
        var memberIds = members.Where(x => !x.IsGuest).Distinct().OrderBy(x => x.Value).ToArray();
        var pending = memberIds.Where(x => !consents.Contains(x)).ToArray();
        return new ChatImportConsentSummary(memberIds.Length - pending.Length, pending.Length,
            pending.Skip(offset).Take(limit).ToApiArray());
    }

    public virtual async Task<ChatImportSession> OnStart(
        ChatImports_Start command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!;

        var userId = await RequireOwner(command.Session, command.ChatId, cancellationToken).ConfigureAwait(false);
        command.Uuid.RequireMaxLength(100);
        var import = await Commander.Call(new ChatsBackend_StartImport(
            command.ChatId, userId, $"{command.ChatId.Value}:{command.Uuid}"), cancellationToken).ConfigureAwait(false);
        await Commander.Call(new ChatImportChangedEvent(import.ChatId, import.Id), cancellationToken)
            .ConfigureAwait(false);
        return import;
    }

    public virtual async Task OnEnd(ChatImports_End command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var import = await Backend.GetImport(command.ChatId, cancellationToken).Require().ConfigureAwait(false);
        var userId = await RequireOwner(command.Session, import.ChatId, cancellationToken).ConfigureAwait(false);
        await Commander.Call(new ChatsBackend_EndImport(import.ChatId, userId, command.ImportId), cancellationToken)
            .ConfigureAwait(false);
        await Commander.Call(new ChatImportChangedEvent(import.ChatId, import.Id), cancellationToken)
            .ConfigureAwait(false);
    }

    public virtual async Task OnSetConsent(ChatImports_SetConsent command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return;

        var import = await Get(command.Session, command.ChatId, cancellationToken).Require().ConfigureAwait(false);
        var account = await Accounts.GetOwn(command.Session, cancellationToken).ConfigureAwait(false);
        await Commander.Call(new ChatsBackend_SetImportConsent(
            import.ChatId, account.Id, command.ImportId, command.HasConsent), cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ApiArray<ChatImportEntryResult>> OnImportEntries(
        ChatImports_ImportEntries command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return default;

        var import = await Get(command.Session, command.ChatId, cancellationToken).Require().ConfigureAwait(false);
        var userId = await RequireOwner(command.Session, import.ChatId, cancellationToken).ConfigureAwait(false);
        return await Commander.Call(new ChatsBackend_ImportEntries(
            command.ChatId, userId, command.ImportId, command.Uuid, command.Entries), cancellationToken)
            .ConfigureAwait(false);
    }

    public virtual async Task<UploadId> OnCreateUpload(
        ChatImports_CreateUpload command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!;

        var import = await Get(command.Session, command.ChatId, cancellationToken).Require().ConfigureAwait(false);
        var userId = await RequireOwner(command.Session, import.ChatId, cancellationToken).ConfigureAwait(false);
        if (!import.IsActive || import.Id != command.ImportId)
            throw StandardError.Constraint("The import session is not active.");
        if (!(await Backend.ListImportConsents(import.ChatId, import.Id, cancellationToken).ConfigureAwait(false))
            .Contains(command.UserId))
            throw StandardError.Constraint("This member has not consented to import.");

        var metadata = new Upload(UploadId.New(), userId, command.Length, command.ChatId.Value, default) {
            FileName = command.FileName,
            ContentType = command.ContentType,
        }.Metadata;
        var uploadId = await Commander.Call(new Uploads_Create {
            Session = command.Session, Length = command.Length,
            Tag = UploadExt.BuildTag(command.ChatId), Metadata = metadata,
        }, cancellationToken).ConfigureAwait(false);
        await Commander.Call(new ChatsBackend_RegisterImportUpload(new ChatImportUpload(
            command.ChatId, import.Id, uploadId, command.UserId, userId, null)), cancellationToken)
                .ConfigureAwait(false);
        return uploadId;
    }

    public virtual async Task<MediaRef> OnFinalizeUpload(
        ChatImports_FinalizeUpload command, CancellationToken cancellationToken)
    {
        if (Invalidation.IsActive)
            return null!;

        var import = await Get(command.Session, command.ChatId, cancellationToken).Require().ConfigureAwait(false);
        var userId = await RequireOwner(command.Session, import.ChatId, cancellationToken).ConfigureAwait(false);
        var upload = await Backend.GetImportUpload(command.ChatId, command.UploadId, cancellationToken)
            .Require().ConfigureAwait(false);
        if (!import.IsActive || import.Id != command.ImportId || upload.ImportId != import.Id
            || upload.UploadedBy != userId)
            throw StandardError.Constraint("The import upload is not available.");

        await Commander.Call(new ChatsBackend_RegisterImportUpload(upload), cancellationToken).ConfigureAwait(false);
        var mediaRef = await Commander.Call(new Uploads_ConvertToMediaRef {
            Session = command.Session, UploadId = upload.UploadId,
        }, cancellationToken).ConfigureAwait(false);
        await Commander.Call(new ChatsBackend_RegisterImportUpload(upload with { Media = mediaRef }), cancellationToken)
            .ConfigureAwait(false);
        return mediaRef;
    }

    // Private methods

    private async Task<UserId> RequireOwner(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
        if (!chat.Rules.IsOwner())
            throw StandardError.Unauthorized("Only owners can manage chat imports.");

        var account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        account.Require(AccountFull.MustBeActive);
        return account.Id;
    }
}
