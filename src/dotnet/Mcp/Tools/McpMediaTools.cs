using System.ComponentModel;
using ActualChat.Mcp.Auth;
using ModelContextProtocol.Server;

namespace ActualChat.Mcp.Tools;

[McpServerToolType]
public sealed class McpMediaTools(IServiceProvider services)
{
    public const int MaxChunkLength = 1024 * 1024;
    public const string HttpClientName = "McpUploadFromUrl";
    private const string UploadTagPrefix = "Mcp/v1/";
    private static readonly TimeSpan ProcessingTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan ProcessingPollDelay = TimeSpan.FromMilliseconds(250);

    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IMedia MediaApi { get; } = services.GetRequiredService<IMedia>();
    private IMediaBackend MediaBackend { get; } = services.GetRequiredService<IMediaBackend>();
    private IUploads Uploads { get; } = services.GetRequiredService<IUploads>();
    private IUploadsBackend UploadsBackend { get; } = services.GetRequiredService<IUploadsBackend>();
    private UrlMapper UrlMapper { get; } = services.GetRequiredService<UrlMapper>();
    private IHttpClientFactory HttpClientFactory { get; } = services.GetRequiredService<IHttpClientFactory>();
    private ICommander Commander { get; } = services.Commander();
    private McpSessionAccessor SessionAccessor { get; } = services.GetRequiredService<McpSessionAccessor>();

    private Session Session => SessionAccessor.Session;

    [McpServerTool(Name = "begin_upload", UseStructuredContent = true)]
    [Description("Starts a chunked upload. Then call append_upload with base64 chunks (at most 1 MB decoded each) " +
        "at increasing offsets, and finish_upload. `purpose`: 'attachment' (default; needs chatId), " +
        "'chat_picture' (chat/place picture or background) or 'avatar_picture'.")]
    public async Task<McpUploadStatus> BeginUpload(
        [Description("File name with extension.")] string fileName,
        [Description("MIME type, e.g. image/png or video/mp4.")] string contentType,
        [Description("Total length in bytes.")] long length,
        [Description("attachment | chat_picture | avatar_picture")] string purpose = "attachment",
        [Description("Chat the attachment is for; required for 'attachment'.")] string? chatId = null,
        CancellationToken cancellationToken = default)
    {
        var (mediaId, uploadId) = await Begin(
            fileName, contentType, length, ParsePurpose(purpose), ChatId.ParseNullable(chatId), cancellationToken)
            .ConfigureAwait(false);
        return new McpUploadStatus(uploadId.Value, mediaId.Value, 0, length, "Uploading", 0, null);
    }

    [McpServerTool(Name = "append_upload", UseStructuredContent = true)]
    [Description("Appends a chunk at `offset`. Returns the server-side offset after the call; if `offset` " +
        "did not match the server's, nothing is written and the current offset is returned so you can resume.")]
    public async Task<McpUploadStatus> AppendUpload(
        [Description("Upload id from begin_upload.")] string uploadId,
        [Description("Byte offset this chunk starts at.")] long offset,
        [Description("Chunk bytes, base64-encoded; at most 1 MB decoded.")] string dataBase64,
        CancellationToken cancellationToken = default)
    {
        var parsedUploadId = UploadId.Parse(uploadId);
        var chunk = Convert.FromBase64String(dataBase64);
        if (chunk.Length > MaxChunkLength)
            throw StandardError.Constraint($"Chunk is larger than {MaxChunkLength} bytes.");

        var upload = await GetOwnUpload(parsedUploadId, cancellationToken).ConfigureAwait(false);
        var serverOffset = await Uploads.GetOffset(Session, parsedUploadId, cancellationToken).ConfigureAwait(false);
        if (serverOffset != offset)
            return ToStatus(upload, serverOffset);

        var command = new Uploads_Append {
            Session = Session,
            UploadId = parsedUploadId,
            Offset = offset,
            Chunk = chunk,
        };
        var newOffset = await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        return ToStatus(upload, newOffset);
    }

    [McpServerTool(Name = "finish_upload", UseStructuredContent = true)]
    [Description("Starts server-side processing (image normalization / video transcoding) and waits up to " +
        "2 minutes for it. Returns stage 'Ready' with `media` set, or the current stage if still processing " +
        "- call again in that case.")]
    public async Task<McpUploadStatus> FinishUpload(
        [Description("Upload id from begin_upload.")] string uploadId,
        CancellationToken cancellationToken = default)
    {
        var parsedUploadId = UploadId.Parse(uploadId);
        var upload = await GetOwnUpload(parsedUploadId, cancellationToken).ConfigureAwait(false);
        return await Finish(upload, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "upload_from_url", UseStructuredContent = true)]
    [Description("Downloads a file from a public http(s) URL server-side and uploads it like " +
        "begin/append/finish_upload would. Same `purpose` and `chatId` rules as begin_upload.")]
    public async Task<McpUploadStatus> UploadFromUrl(
        [Description("Public http(s) URL of the file.")] string url,
        [Description("attachment | chat_picture | avatar_picture")] string purpose = "attachment",
        [Description("Chat the attachment is for; required for 'attachment'.")] string? chatId = null,
        [Description("File name to store; defaults to the URL's last segment.")] string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !EgressHttpHandler.IsHttpUri(uri))
            throw StandardError.Constraint("url must be an absolute http(s) URL.");

        using var httpClient = HttpClientFactory.CreateClient(HttpClientName);
        using var response = await httpClient
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        var name = fileName.NullIfEmpty()
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"').NullIfEmpty()
            ?? uri.Segments[^1].Trim('/').NullIfEmpty()
            ?? "file";
        // Uploads_Create needs the length up front, so the body is buffered (bounded by the handler's response cap)
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var (mediaId, uploadId) = await Begin(
            name, contentType, bytes.Length, ParsePurpose(purpose), ChatId.ParseNullable(chatId), cancellationToken)
            .ConfigureAwait(false);
        for (var offset = 0; offset < bytes.Length; offset += MaxChunkLength) {
            var chunk = bytes.AsSpan(offset, Math.Min(MaxChunkLength, bytes.Length - offset)).ToArray();
            await Commander.Call(new Uploads_Append {
                Session = Session,
                UploadId = uploadId,
                Offset = offset,
                Chunk = chunk,
            }, cancellationToken).ConfigureAwait(false);
        }
        var upload = await GetOwnUpload(uploadId, cancellationToken).ConfigureAwait(false);
        return await Finish(upload, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "abort_upload", UseStructuredContent = true)]
    [Description("Discards an unfinished upload.")]
    public async Task AbortUpload(
        [Description("Upload id from begin_upload.")] string uploadId,
        CancellationToken cancellationToken = default)
    {
        var command = new Uploads_Remove { Session = Session, UploadId = UploadId.Parse(uploadId) };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "list_media", UseStructuredContent = true)]
    [Description("Lists photos and videos posted in a chat, newest period (month) first. Omit `periodKey` " +
        "for the newest period; use `nextPeriodKey` to go older and `pageIndex` < `pageCount` to page within a period.")]
    public Task<McpContentPage<McpMediaItem>> ListMedia(
        [Description("The chat id.")] string chatId,
        [Description("Period key from a previous result.")] string? periodKey = null,
        [Description("Page within the period, 0-based.")] int pageIndex = 0,
        CancellationToken cancellationToken = default)
        => ListContent(chatId, ChatContentKind.Media, periodKey, pageIndex,
            (id, key, page, ct) => Chats.GetVisualMediaPeriod(Session, id, key, page, ct),
            item => item.ToMcpModel(UrlMapper),
            cancellationToken);

    [McpServerTool(Name = "list_files", UseStructuredContent = true)]
    [Description("Lists files posted in a chat, newest period (month) first. Paging works like list_media.")]
    public Task<McpContentPage<McpFileItem>> ListFiles(
        [Description("The chat id.")] string chatId,
        [Description("Period key from a previous result.")] string? periodKey = null,
        [Description("Page within the period, 0-based.")] int pageIndex = 0,
        CancellationToken cancellationToken = default)
        => ListContent(chatId, ChatContentKind.File, periodKey, pageIndex,
            (id, key, page, ct) => Chats.GetFilePeriod(Session, id, key, page, ct),
            item => item.ToMcpModel(UrlMapper),
            cancellationToken);

    [McpServerTool(Name = "list_links", UseStructuredContent = true)]
    [Description("Lists links posted in a chat, newest period (month) first. Paging works like list_media.")]
    public Task<McpContentPage<McpLinkItem>> ListLinks(
        [Description("The chat id.")] string chatId,
        [Description("Period key from a previous result.")] string? periodKey = null,
        [Description("Page within the period, 0-based.")] int pageIndex = 0,
        CancellationToken cancellationToken = default)
        => ListContent(chatId, ChatContentKind.Link, periodKey, pageIndex,
            (id, key, page, ct) => Chats.GetLinkPeriod(Session, id, key, page, ct),
            item => item.ToMcpModel(),
            cancellationToken);

    // Private methods

    private async Task<McpContentPage<TOut>> ListContent<TIn, TOut>(
        string chatId,
        ChatContentKind kind,
        string? periodKey,
        int pageIndex,
        Func<ChatId, string, int, CancellationToken, Task<TIn[]>> getPage,
        Func<TIn, TOut> map,
        CancellationToken cancellationToken)
    {
        var parsedChatId = ChatId.Parse(chatId);
        // Each skeleton covers one calendar year; walk back until the year holding the requested period
        var skeleton = await Chats
            .GetContentPeriods(Session, parsedChatId, kind, beforePeriodKey: null, cancellationToken)
            .ConfigureAwait(false);
        while (true) {
            var isFound = periodKey is null
                ? skeleton.Periods.Length > 0
                : skeleton.Periods.Any(p => p.PeriodKey == periodKey);
            if (isFound || skeleton.NextPeriodKey is null)
                break;

            skeleton = await Chats
                .GetContentPeriods(Session, parsedChatId, kind, skeleton.NextPeriodKey, cancellationToken)
                .ConfigureAwait(false);
        }

        var periods = skeleton.Periods;
        var index = periodKey is null ? 0 : Array.FindIndex(periods, p => p.PeriodKey == periodKey);
        if (index < 0 || periods.Length == 0)
            return new McpContentPage<TOut>(periodKey ?? "", pageIndex, 0, null, []);

        var period = periods[index];
        var nextPeriodKey = index + 1 < periods.Length ? periods[index + 1].PeriodKey : skeleton.NextPeriodKey;
        var items = pageIndex < period.PageCount
            ? await getPage(parsedChatId, period.PeriodKey, pageIndex, cancellationToken).ConfigureAwait(false)
            : [];
        return new McpContentPage<TOut>(
            period.PeriodKey, pageIndex, period.PageCount, nextPeriodKey, items.Select(map).ToArray());
    }

    private async Task<(MediaId MediaId, UploadId UploadId)> Begin(
        string fileName,
        string contentType,
        long length,
        McpUploadPurpose purpose,
        ChatId? chatId,
        CancellationToken cancellationToken)
    {
        if (purpose == McpUploadPurpose.Attachment && chatId is null)
            throw StandardError.Constraint("chatId is required for attachment uploads.");

        var account = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        var scope = chatId?.Value ?? account.Id.Value;
        var metadata = new MetadataBag()
            .Set(nameof(Media.Media.FileName), fileName)
            .Set(nameof(Media.Media.ContentType), contentType)
            .Set(nameof(Media.Media.Length), length);
        var mediaId = await Commander.Call(new Media_ReserveMedia {
            Session = Session,
            Scope = scope,
            Metadata = metadata,
            Kind = purpose switch {
                McpUploadPurpose.ChatPicture => MediaKind.ChatPicture,
                McpUploadPurpose.AvatarPicture => MediaKind.UserAvatarPicture,
                _ => MediaKind.ChatEntryAttachment,
            },
        }, cancellationToken).ConfigureAwait(false);
        var uploadId = await Commander.Call(new Uploads_Create {
            Session = Session,
            Length = length,
            Tag = UploadTagPrefix + mediaId.Value,
            Metadata = metadata,
        }, cancellationToken).ConfigureAwait(false);
        return (mediaId, uploadId);
    }

    private async Task<McpUploadStatus> Finish(Upload upload, CancellationToken cancellationToken)
    {
        var mediaId = GetMediaId(upload);
        var progress = await MediaApi.GetProgress(Session, mediaId, cancellationToken).Require().ConfigureAwait(false);
        if (progress.Stage < MediaProcessingStage.ServerProcessing)
            await Commander.Call(new Uploads_StartProcessUpload {
                Session = Session,
                UploadId = upload.Id,
                MediaId = mediaId,
            }, cancellationToken).ConfigureAwait(false);

        var deadline = CpuTimestamp.Now + ProcessingTimeout;
        while (true) {
            progress = await MediaApi.GetProgress(Session, mediaId, cancellationToken).Require().ConfigureAwait(false);
            if (progress.HasFailed)
                throw StandardError.Internal($"Media processing failed: {progress.Error}");
            if (progress.Stage == MediaProcessingStage.Ready)
                break;
            if (CpuTimestamp.Now > deadline)
                return new McpUploadStatus(upload.Id.Value, mediaId.Value, 0, upload.Length ?? 0,
                    progress.Stage.ToString(), progress.StageProgress, null);

            await Task.Delay(ProcessingPollDelay, cancellationToken).ConfigureAwait(false);
        }

        var mediaRef = await ToMediaRef(mediaId, cancellationToken).ConfigureAwait(false);
        var length = mediaRef.Length;
        return new McpUploadStatus(upload.Id.Value, mediaId.Value, length, length, "Ready", 100, mediaRef);
    }

    private async Task<McpMediaRef> ToMediaRef(MediaId mediaId, CancellationToken cancellationToken)
    {
        // GetContent enforces ownership; MediaBackend fills in dimensions the session API doesn't expose
        var content = await MediaApi.GetContent(Session, mediaId, cancellationToken).Require().ConfigureAwait(false);
        var media = await MediaBackend.Get(mediaId, cancellationToken).Require().ConfigureAwait(false);
        var thumbnail = content.ThumbnailMediaId is { } thumbnailId
            ? await MediaBackend.Get(thumbnailId, cancellationToken).ConfigureAwait(false)
            : null;
        return media.ToMcpMediaRef(thumbnail, UrlMapper);
    }

    private async Task<Upload> GetOwnUpload(UploadId uploadId, CancellationToken cancellationToken)
    {
        // The backend read is unauthenticated, so ownership and origin are checked here
        var account = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        var upload = await UploadsBackend.Get(uploadId, cancellationToken).ConfigureAwait(false);
        if (upload is null || upload.UserId != account.Id || !upload.Tag.StartsWith(UploadTagPrefix))
            throw StandardError.Upload.NotFound();

        return upload;
    }

    private static McpUploadStatus ToStatus(Upload upload, long offset)
    {
        var length = upload.Length ?? 0;
        var progress = length == 0 ? 0 : 100.0 * offset / length;
        var mediaId = GetMediaId(upload);
        return new McpUploadStatus(upload.Id.Value, mediaId.Value, offset, length, "Uploading", progress, null);
    }

    private static MediaId GetMediaId(Upload upload)
        => MediaId.Parse(upload.Tag[UploadTagPrefix.Length..]);

    private static McpUploadPurpose ParsePurpose(string purpose)
        => purpose.ToLower() switch {
            "attachment" => McpUploadPurpose.Attachment,
            "chat_picture" => McpUploadPurpose.ChatPicture,
            "avatar_picture" => McpUploadPurpose.AvatarPicture,
            _ => throw StandardError.Constraint("purpose must be attachment, chat_picture or avatar_picture."),
        };

    // Nested types

    private enum McpUploadPurpose
    {
        Attachment,
        ChatPicture,
        AvatarPicture,
    }
}
