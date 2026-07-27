using ActualChat.AspNetCore;
using ActualChat.Resilience;
using ActualChat.Security;
using ActualChat.Uploads;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Chat.Controllers;

[ApiController, Route("api/chat-media")]
public sealed class ChatMediaController(IServiceProvider services) : ControllerBase
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IMediaProcessor MediaProcessor { get; } = services.GetRequiredService<IMediaProcessor>();
    private IMediaSaver MediaSaver { get; } = services.GetRequiredService<IMediaSaver>();
    private RateLimitPolicy RateLimitPolicy => field ??= services.GetRequiredService<RateLimitPolicy>();
    private RateLimitIdentityResolver IdentityResolver
        => field ??= services.GetRequiredService<RateLimitIdentityResolver>();

    [HttpPost("{chatId}/upload")]
    [RequestSizeLimit(Constants.Attachments.FileSizeLimit * 2)]
    [RequestFormLimits(MultipartBodyLengthLimit = Constants.Attachments.FileSizeLimit * 2)]
    public async Task<ActionResult<MediaRef>> Upload(ChatId chatId, CancellationToken cancellationToken)
    {
        try {
            // NOTE(AY): Header is used by clients, cookie is used by SSB
            var session = HttpContext.TryGetSessionFromHeader(SessionFormat.Token)
                ?? HttpContext.GetSessionFromCookie();
            await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
            await Authors.EnsureJoined(session, chatId, cancellationToken).ConfigureAwait(false);
            var chat = await Chats.Get(session, chatId, cancellationToken).Require().ConfigureAwait(false);
            chat.Rules.Permissions.Require(ChatPermissions.Write | ChatPermissions.Upload);
        }
        catch (Exception e) {
            return BadRequest(e.Message);
        }

        var httpRequest = HttpContext.Request;
        if (!httpRequest.HasFormContentType || httpRequest.Form.Files.Count == 0)
            return BadRequest("No file found.");

        if (httpRequest.Form.Files.Count > 1)
            return BadRequest("Too many files.");

        var file = httpRequest.Form.Files[0];
        if (file.Length == 0)
            return BadRequest("File is empty.");

        if (file.Length > Constants.Attachments.FileSizeLimit)
            return BadRequest("File is too big.");

        try {
            await RateLimitPolicy
                .CheckUpload(
                    IdentityResolver,
                    $"{nameof(ChatMediaController)}.{nameof(Upload)}",
                    RateLimitSource.ForHttp(HttpContext),
                    file.Length,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RateLimitExceededException e) {
            Response.SetRetryAfter(e.RetryDelay);
            return StatusCode(StatusCodes.Status429TooManyRequests, e.Message);
        }

        var uploadedFile = new UploadedStreamFile(
            file.FileName,
            file.ContentType,
            file.Length,
            () => Task.FromResult(file.OpenReadStream()));
        using var processedFile = await MediaProcessor
            .ProcessUpload(uploadedFile, MediaKind.ChatEntryAttachment, null, cancellationToken)
            .ConfigureAwait(false);
        var mediaRef = await MediaSaver
            .Save(MediaId.New(chatId.Value), processedFile, isUpdate:false, MediaKind.ChatEntryAttachment, cancellationToken)
            .ConfigureAwait(false);
        return Ok(mediaRef);
    }
}
