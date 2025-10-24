using Foundation;

namespace ActualChat.App.Maui;

// ReSharper disable once InconsistentNaming
public static class NSErrorExt
{
    public static Exception? ToException(this NSError? error, string? message = null)
        => error != null
            ? StandardError.External(message.NullIfEmpty() ?? $"{error.LocalizedDescription}. {error.Description}",
                new NSErrorException(error))
            : null;

    public static void Assert(this NSError? error, string? message = null)
    {
        if (error.ToException(message) is { } ex)
            throw ex;
    }
}
