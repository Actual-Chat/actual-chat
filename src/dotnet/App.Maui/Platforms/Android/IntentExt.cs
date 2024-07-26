using Android.Content;

namespace ActualChat.App.Maui;

public static class IntentExt
{
    public static bool IsFromHistory(this Intent? intent)
        // NOTE: see https://stackoverflow.com/questions/4866149/android-starting-app-from-recent-applications-starts-it-with-the-last-set-of
        => intent is not null && (intent.Flags & ActivityFlags.LaunchedFromHistory) != 0;
}
