using System.Security;
using ActualChat.Localization;
using ActualChat.Maui;

namespace ActualChat.App.Maui.IosShareExt.Services;

public static class ExceptionExt
{
    extension(Exception error)
    {
        public string UserFriendlyMessage => error switch
        {
            SecurityException or UnauthorizedAccessException => AppStrings.L.ShareExt_CannotSendToContact,
            _ => AppStrings.L.ShareExt_SharingFailed,
        };
    }
}
