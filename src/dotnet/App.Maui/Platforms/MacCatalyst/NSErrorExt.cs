using Foundation;

namespace ActualChat.App.Maui;

// ReSharper disable once InconsistentNaming
public static class NSErrorExt
{
    public static Exception? ToException(this NSError? error)
        => error != null
            ? new InternalError($"{error.LocalizedDescription}. {error.Description}", new NSErrorException(error))
            : null;

    public static void ThrowIfError(this NSError? error)
    {
        if (error != null)
            throw new InternalError($"{error.LocalizedDescription}. {error.Description}", new NSErrorException(error));
    }
}
