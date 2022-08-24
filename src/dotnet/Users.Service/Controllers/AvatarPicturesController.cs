using ActualChat.Web.Internal;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Users.Controllers;

[ApiController]
public class AvatarPicturesController : UploadControllerBase
{
    private IUserAvatars UserAvatars { get; }

    public AvatarPicturesController(IUserAvatars userAvatars) => UserAvatars = userAvatars;

    [HttpPost, Route("api/user-avatars/{avatarId}/upload-picture")]
    public Task<IActionResult> UploadPicture(string avatarId, CancellationToken cancellationToken)
    {
        return Upload(ValidateRequest, GetContentIdPrefix, cancellationToken);

        async Task<IActionResult?> ValidateRequest()
        {
            var userAvatar = await UserAvatars.Get(SessionResolver.Session, avatarId, cancellationToken).ConfigureAwait(false);
            return userAvatar is null ? NotFound() : null;
        }

        string GetContentIdPrefix() => $"avatar-pictures/{avatarId.Replace(':', '_')}/picture-";
    }
}
