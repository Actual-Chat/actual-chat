using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using ActualChat.Mcp.Auth;
using ModelContextProtocol.Server;

namespace ActualChat.Mcp.Tools;

[McpServerToolType]
public sealed class McpImportTools(IServiceProvider services)
{
    private const int MaxPendingLimit = 100;
    private const int MaxStatusAttempts = 3;

    private IChatImports ChatImports { get; } = services.GetRequiredService<IChatImports>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IMediaBackend MediaBackend { get; } = services.GetRequiredService<IMediaBackend>();
    private UrlMapper UrlMapper { get; } = services.GetRequiredService<UrlMapper>();
    private IHttpClientFactory HttpClientFactory { get; } = services.GetRequiredService<IHttpClientFactory>();
    private ICommander Commander { get; } = services.Commander();
    private McpSessionAccessor SessionAccessor { get; } = services.GetRequiredService<McpSessionAccessor>();

    private Session Session => SessionAccessor.Session;

    [McpServerTool(Name = "get_import", UseStructuredContent = true)]
    [Description("Returns the import session running for a chat, if any: its id, who started it and when, " +
        "and how many members have consented. A Place-wide import is reported for every chat of the " +
        "Place, with `scope` 'place' and `placeId`. Owners also get `pending`, a page of members who have " +
        "not consented yet, each with `peerChatId` - your direct chat with them, where post_message works " +
        "during the import. Poll this while waiting for consent.")]
    public async Task<McpImportStatus> GetImport(
        [Description("A group chat, a Place chat or a Place's root chat.")] string chatId,
        [Description("Index of the first pending member to return.")] int offset = 0,
        [Description("Max pending members to return; at most 100.")] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var parsedChatId = ChatId.Parse(chatId);
        offset = Math.Max(offset, 0);
        limit = Math.Clamp(limit, 1, MaxPendingLimit);
        // The session can end or be replaced between the reads, so a status is returned only
        // if the same session is still running once they are done
        for (var attempt = 1; ; attempt++) {
            var import = await ChatImports.Get(Session, parsedChatId, cancellationToken).ConfigureAwait(false);
            if (import is null)
                return new McpImportStatus(false);

            var status = await ReadStatus(parsedChatId, import, offset, limit, cancellationToken)
                .ResultAwait(false);
            var current = await ChatImports.Get(Session, parsedChatId, cancellationToken).ConfigureAwait(false);
            if (current?.ImportId == import.ImportId)
                return status.Value;
            if (attempt == MaxStatusAttempts)
                throw StandardError.Constraint("The import session keeps changing; call get_import again.");
        }
    }

    [McpServerTool(Name = "start_import", UseStructuredContent = true)]
    [Description("Starts importing history into a group chat or a single Place chat (`scope` 'chat'), or " +
        "into every chat of a Place when given the Place's root chat (`scope` 'place'). Owners only. " +
        "WARNING: until end_import, nobody can post in the chat - or, for a Place-wide import, in any chat " +
        "of the Place - including you. Confirm with the user before calling, then pass `confirm: true`; " +
        "without it nothing happens. The call takes about 10 seconds and fails if someone posts meanwhile. " +
        "Pass the same `requestId` to retry a call whose answer you did not get, and a new one for each " +
        "new import.")]
    public async Task<McpImportSession> StartImport(
        [Description("A group chat, a Place chat or a Place's root chat.")] string chatId,
        [Description("True once the user agreed that posting stops for everyone.")] bool confirm = false,
        [Description("Your request id; a retry with it returns the same session.")] string? requestId = null,
        CancellationToken cancellationToken = default)
    {
        if (!confirm)
            throw StandardError.Constraint(
                "Nothing was started. An import stops posting for every member - for a Place-wide import, "
                + "in every chat of the Place - until it ends. Confirm with the user, then call again with "
                + "confirm: true.");

        var parsedChatId = ChatId.Parse(chatId);
        var command = new ChatImports_Start { Session = Session, ChatId = parsedChatId };
        if (!requestId.IsNullOrEmpty()) {
            var account = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
            command = command with { Uuid = ToUuid($"{account.Id.Value}:{parsedChatId.Value}:{requestId}") };
        }
        var import = await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        return new McpImportSession(
            import.ImportId.Value,
            import.ChatId.Value,
            GetScope(import),
            import.StartedBy?.Value,
            import.StartedAt.ToMcpMillis());
    }

    [McpServerTool(Name = "consent_to_import", UseStructuredContent = true)]
    [Description("Allows the import session to import your own messages. Only your own consent can be " +
        "given, and it cannot be revoked for that session. Owners must consent too, to import their own " +
        "messages.")]
    public async Task ConsentToImport(
        [Description("The chat the import runs for.")] string chatId,
        [Description("Import id from get_import.")] string importId,
        CancellationToken cancellationToken = default)
    {
        var command = new ChatImports_SetConsent {
            Session = Session,
            ChatId = ChatId.Parse(chatId),
            ImportId = ChatImportId.Parse(importId),
            HasConsent = true,
        };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "end_import", UseStructuredContent = true)]
    [Description("Ends the import session, so members can post again. Owners only; allowed at any time. " +
        "Members who never consented simply have no messages imported. Returns how many had not consented.")]
    public async Task<McpImportEndResult> EndImport(
        [Description("The chat the import runs for.")] string chatId,
        [Description("Import id from get_import or start_import.")] string importId,
        CancellationToken cancellationToken = default)
    {
        var parsedChatId = ChatId.Parse(chatId);
        var summary = await ChatImports
            .GetConsentSummary(Session, parsedChatId, 0, 1, cancellationToken)
            .ConfigureAwait(false);
        var command = new ChatImports_End {
            Session = Session,
            ChatId = parsedChatId,
            ImportId = ChatImportId.Parse(importId),
        };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        return new McpImportEndResult(summary.NonConsentingCount);
    }

    [McpServerTool(Name = "begin_import_upload", UseStructuredContent = true)]
    [Description("Starts a chunked upload of an attachment for an imported message by `authorUserId`, who " +
        "must have consented. `chatId` must be the chat you will pass to import_messages - for a " +
        "Place-wide import, the Place chat, not the root. Send the bytes with append_upload, then call " +
        "finish_import_upload, and pass the upload id in import_messages. Each upload attaches to one message only.")]
    public async Task<McpUploadStatus> BeginImportUpload(
        [Description("The chat the message is imported into.")] string chatId,
        [Description("Import id from get_import.")] string importId,
        [Description("User id of the member who wrote the message.")] string authorUserId,
        [Description("File name with extension.")] string fileName,
        [Description("MIME type, e.g. image/png or video/mp4.")] string contentType,
        [Description("Total length in bytes.")] long length,
        CancellationToken cancellationToken = default)
    {
        var uploadId = await CreateUpload(
                chatId,
                importId,
                authorUserId,
                fileName,
                contentType,
                length,
                cancellationToken)
            .ConfigureAwait(false);
        return new McpUploadStatus(uploadId.Value, null, 0, length, "Uploading", 0, null);
    }

    [McpServerTool(Name = "finish_import_upload", UseStructuredContent = true)]
    [Description("Processes an upload from begin_import_upload once all its bytes are appended, and returns " +
        "it in stage 'Ready'.")]
    public async Task<McpUploadStatus> FinishImportUpload(
        [Description("The chat the message is imported into.")] string chatId,
        [Description("Import id from get_import.")] string importId,
        [Description("Upload id from begin_import_upload.")] string uploadId,
        CancellationToken cancellationToken = default)
    {
        var command = new ChatImports_FinalizeUpload {
            Session = Session,
            ChatId = ChatId.Parse(chatId),
            ImportId = ChatImportId.Parse(importId),
            UploadId = UploadId.Parse(uploadId),
        };
        var mediaRef = await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        return await ToReadyStatus(command.UploadId, mediaRef, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "import_upload_from_url", UseStructuredContent = true)]
    [Description("Downloads a file from a public http(s) URL server-side and uploads it as an attachment " +
        "for an imported message by `authorUserId`, like begin_import_upload, append_upload and " +
        "finish_import_upload would.")]
    public async Task<McpUploadStatus> ImportUploadFromUrl(
        [Description("Public http(s) URL of the file.")] string url,
        [Description("The chat the message is imported into.")] string chatId,
        [Description("Import id from get_import.")] string importId,
        [Description("User id of the member who wrote the message.")] string authorUserId,
        [Description("File name; defaults to the URL's last segment.")] string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        var file = await McpMediaTools
            .Download(HttpClientFactory, url, fileName, cancellationToken)
            .ConfigureAwait(false);
        var uploadId = await CreateUpload(
                chatId,
                importId,
                authorUserId,
                file.FileName,
                file.ContentType,
                file.Data.Length,
                cancellationToken)
            .ConfigureAwait(false);
        await McpMediaTools.AppendAll(Commander, Session, uploadId, file.Data, cancellationToken).ConfigureAwait(false);
        return await FinishImportUpload(chatId, importId, uploadId.Value, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "import_messages", UseStructuredContent = true)]
    [Description("Imports up to 100 messages with their original authors and dates. Owners only. " +
        "Messages must be dated in the past and later than the chat's newest message - system messages " +
        "such as 'joined' count, so in a group chat the dates must be later than its members joined - " +
        "and later than those imported by earlier calls, so send batches forward in time. Within a batch " +
        "the order does not matter, but no two messages may share a date. A reply target must already be " +
        "in the chat: to reply to a message of the same batch, import the target in an earlier batch and " +
        "use the id it returned. A message whose author left, is not a member or has not consented is " +
        "rejected on its own; the call still succeeds and reports it in `error`. Resending a `batchId` " +
        "returns its earlier result. This is not post_message, which posts as you, dated now. Imported " +
        "messages notify no one.")]
    public async Task<McpImportMessagesResult> ImportMessages(
        [Description("A group chat or a Place chat, not a root chat.")] string chatId,
        [Description("Import id from get_import.")] string importId,
        [Description("Your id for this batch, up to 100 characters.")] string batchId,
        [Description("1 to 100 messages. `sentAt`: Unix time in milliseconds, like `createdAt` in list_messages. " +
            "`text`: Voxt markup, up to 64 KB. `replyToId`: id of a message already in the chat. " +
            "`uploadIds`: up to 10, from begin_import_upload or import_upload_from_url.")]
        McpImportMessage[] messages,
        CancellationToken cancellationToken = default)
    {
        if (batchId.IsNullOrEmpty())
            throw StandardError.Constraint("batchId is required.");

        var entries = messages.Select(ToEntry).ToApiArray();
        var command = new ChatImports_ImportEntries {
            Session = Session,
            Uuid = batchId,
            ChatId = ChatId.Parse(chatId),
            ImportId = ChatImportId.Parse(importId),
            Entries = entries,
        };
        var results = await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        var items = results
            .Select(r => r.Error.IsNone
                ? new McpImportMessageResult(r.EntryId?.LocalId, null)
                : new McpImportMessageResult(null, r.Error.Message))
            .ToArray();
        return new McpImportMessagesResult(items);
    }

    // Private methods

    private async Task<McpImportStatus> ReadStatus(
        ChatId chatId, ChatImportSession import, int offset, int limit, CancellationToken cancellationToken)
    {
        var summary = await ChatImports
            .GetConsentSummary(Session, chatId, offset, limit, cancellationToken)
            .ConfigureAwait(false);
        var hasConsent = await ChatImports
            .HasConsent(Session, chatId, import.ImportId, cancellationToken)
            .ConfigureAwait(false);
        var scope = await Chats.Get(Session, import.ChatId, cancellationToken).ConfigureAwait(false);
        var pending = scope?.Rules.IsOwner() == true
            ? await ToPendingMembers(summary.NonConsentingUserIds, cancellationToken).ConfigureAwait(false)
            : null;
        var memberCount = summary.ConsentingCount + summary.NonConsentingCount;
        var consentPercent = memberCount == 0 ? 0 : Math.Round(100.0 * summary.ConsentingCount / memberCount, 1);
        return new McpImportStatus(
            true,
            import.ImportId.Value,
            GetScope(import),
            import.ChatId is PlaceChatId { IsRoot: true } rootChatId ? rootChatId.PlaceId.Value : null,
            import.StartedBy?.Value,
            import.StartedAt.ToMcpMillis(),
            summary.ConsentingCount,
            summary.NonConsentingCount,
            consentPercent,
            hasConsent,
            pending);
    }

    private async Task<McpImportPendingMember[]> ToPendingMembers(
        ApiArray<UserId> userIds, CancellationToken cancellationToken)
    {
        var ownAccount = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        return await Task
            .WhenAll(userIds.Select(id => ToPendingMember(id, ownAccount.Id, cancellationToken)))
            .ConfigureAwait(false);
    }

    private async Task<McpImportPendingMember> ToPendingMember(
        UserId userId, UserId ownUserId, CancellationToken cancellationToken)
    {
        var account = await Accounts.Get(Session, userId, cancellationToken).ConfigureAwait(false);
        string? peerChatId = null;
        if (userId != ownUserId) {
            var peerChat = await Chats
                .Get(Session, PeerChatId.New(ownUserId, userId), cancellationToken)
                .ConfigureAwait(false);
            peerChatId = peerChat?.Id.Value;
        }
        return new McpImportPendingMember(userId.Value, account?.Avatar?.Name ?? "", peerChatId);
    }

    private async Task<UploadId> CreateUpload(
        string chatId,
        string importId,
        string authorUserId,
        string fileName,
        string contentType,
        long length,
        CancellationToken cancellationToken)
    {
        var command = new ChatImports_CreateUpload {
            Session = Session,
            ChatId = ChatId.Parse(chatId),
            ImportId = ChatImportId.Parse(importId),
            UserId = UserId.Parse(authorUserId),
            FileName = fileName,
            ContentType = contentType,
            Length = length,
        };
        return await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpUploadStatus> ToReadyStatus(
        UploadId uploadId, MediaRef mediaRef, CancellationToken cancellationToken)
    {
        // The media belongs to the imported author, so it is read from the backend rather than through the session
        var media = await MediaBackend.Get(mediaRef.MediaId, cancellationToken).Require().ConfigureAwait(false);
        var thumbnail = mediaRef.ThumbnailMediaId is { } thumbnailId
            ? await MediaBackend.Get(thumbnailId, cancellationToken).ConfigureAwait(false)
            : null;
        var mcpMediaRef = media.ToMcpMediaRef(thumbnail, UrlMapper);
        var length = mcpMediaRef.Length;
        return new McpUploadStatus(uploadId.Value, media.Id.Value, length, length, "Ready", 100, mcpMediaRef);
    }

    private static ChatImportEntry ToEntry(McpImportMessage message)
        => new(UserId.Parse(message.AuthorUserId), message.SentAt.FromMcpMillis(), message.Text) {
            UploadIds = (message.UploadIds ?? []).Select(UploadId.Parse).ToApiArray(),
            RepliedEntryLid = message.ReplyToId,
        };

    private static string GetScope(ChatImportSession import)
        => import.ChatId is PlaceChatId { IsRoot: true } ? "place" : "chat";

    private static string ToUuid(string key)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];
}
