using Foundation;

namespace ActualChat.App.Maui;

public static class NSErrorExt
{
    public static void Assert(this NSError? error)
    {
        if (error != null)
            throw new InternalError($"{error.LocalizedDescription}. {error.Description}", new NSErrorException(error));
    }
}
