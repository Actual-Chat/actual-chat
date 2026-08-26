using System.Security;
using ActualChat.App.Maui.IosShareExt.UI;
using ActualChat.Localization;

namespace ActualChat.App.Maui.IosShareExt.Services;

public static class ExceptionExt
{
    extension(Exception error)
    {
        public string UserFriendlyMessage => error switch
        {
            SecurityException or UnauthorizedAccessException => AppStrings.L.ShareExt_CannotSendToContact,
            _ => AppStrings.L.ShareExt_UploadFailed,
        };
    }
}
