using System.Security;

namespace ActualChat.App.Maui.IosShareExt.Services;

public static class ExceptionExt
{
    extension(Exception error)
    {
        public string UserFriendlyMessage => error switch
        {
            SecurityException or UnauthorizedAccessException => "You can't send to this contact",
            _ => "Upload failed",
        };
    }
}
