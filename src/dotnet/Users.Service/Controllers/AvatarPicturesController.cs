using System.ComponentModel.DataAnnotations;
using ActualChat.AspNetCore;
using ActualChat.Controllers;
using ActualChat.Resilience;
using ActualChat.Security;
using ActualChat.Uploads;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Users.Controllers;

[ApiController, Route("api/avatars")]
public sealed class AvatarPicturesController(IServiceProvider services) : ControllerBase
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IMediaProcessor MediaProcessor => services.GetRequiredService<IMediaProcessor>();
    private IMediaSaver MediaSaver => services.GetRequiredService<IMediaSaver>();
    private AvatarPictures AvatarPictures => services.GetRequiredService<AvatarPictures>();
    private RateLimitPolicy RateLimitPolicy => field ??= services.GetRequiredService<RateLimitPolicy>();
    private RateLimitIdentityResolver IdentityResolver
        => field ??= services.GetRequiredService<RateLimitIdentityResolver>();

    [HttpPost("upload-picture")]
    [DisableFormValueModelBinding]
    [RequestSizeLimit(Constants.Attachments.AvatarPictureFileSizeLimit * 2)]
    [RequestFormLimits(MultipartBodyLengthLimit = Constants.Attachments.AvatarPictureFileSizeLimit * 2)]
    public async Task<ActionResult<MediaRef>> UploadPicture(CancellationToken cancellationToken)
    {
        AccountFull account;
        try {
            // NOTE(AY): Header is used by clients, cookie is used by SSB
            var session = HttpContext.TryGetSessionFromHeader(SessionFormat.Token)
                ?? HttpContext.GetSessionFromCookie();
            account = await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
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
            return BadRequest("Image is empty.");

        if (file.Length > Constants.Attachments.AvatarPictureFileSizeLimit)
            return BadRequest("Image is too big.");

        if (!MediaTypeExt.SupportedAvatarContentTypes.Contains(file.ContentType))
            return BadRequest($"Unsupported image type '{file.ContentType}'.");

        try {
            await RateLimitPolicy
                .CheckUpload(
                    IdentityResolver,
                    $"{nameof(AvatarPicturesController)}.{nameof(UploadPicture)}",
                    RateLimitSource.ForHttp(HttpContext),
                    file.Length,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RateLimitExceededException e) {
            Response.SetRetryAfter(e.RetryDelay);
            return StatusCode(StatusCodes.Status429TooManyRequests, e.Message);
        }

        var mediaId = MediaId.New(account.Id.Value);
        var uploadedFile = new UploadedStreamFile(
            file.FileName,
            file.ContentType,
            file.Length,
            () => Task.FromResult(file.OpenReadStream()));

        using var processed = await MediaProcessor
            .ProcessUpload(uploadedFile, MediaKind.UserAvatarPicture, null, cancellationToken)
            .ConfigureAwait(false);
        var mediaRef = await MediaSaver
            .Save(mediaId, processed, false, MediaKind.UserAvatarPicture, cancellationToken)
            .ConfigureAwait(false);
        return Ok(mediaRef);
    }

    [HttpGet("{kind}/{key}")]
    [CacheControlImmutable(Duration = 2592000, VaryByQueryKeys = ["*"])] // 30 days
    public async Task<ActionResult> GetAvatar(
        AvatarKind kind,
        string key,
        AvatarFormat format,
        int? size = null,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        var query = new AvatarQuery {
            Kind = kind,
            Key = key,
            Format = format,
            Size = size,
            Title = title,
        };
        // TryValidateModel short-circuits here: the scalar parameters above already
        // filled ModelState, so its root entry is no longer Unvalidated.
        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(query, new ValidationContext(query), validationResults, true)) {
            foreach (var validationResult in validationResults)
                ModelState.AddModelError(
                    validationResult.MemberNames.FirstOrDefault() ?? "",
                    validationResult.ErrorMessage ?? "Invalid value.");
            return BadRequest(ModelState);
        }

        var filePath = await AvatarPictures.Get(query, cancellationToken).ConfigureAwait(false);
        return PhysicalFile(filePath, MediaMimeTypes.GetMimeType(filePath));
    }
}
