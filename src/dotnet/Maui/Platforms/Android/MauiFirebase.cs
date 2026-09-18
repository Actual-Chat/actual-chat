using Android.Content;
using Firebase;

namespace ActualChat.Maui;

/// <summary>
/// Owns Firebase initialization on Android. <c>FirebaseInitProvider</c> is removed from the
/// manifest, so <see cref="FirebaseApp"/> - and the Crashlytics component it eagerly builds -
/// initializes on this thread instead of on the main thread before <c>Application.onCreate</c>.
/// </summary>
public static class MauiFirebase
{
    private static readonly TaskCompletionSource WhenReadySource = TaskCompletionSourceExt.New();
    private static int _isStarted;

    public static Task WhenReady => WhenReadySource.Task;
    public static bool IsReady => WhenReady.IsCompletedSuccessfully;

    public static void Start(Context context)
    {
        // A dedicated thread rather than the pool: this runs from Application.onCreate, where on an
        // FCM wake the ThreadPool spin-up alone competes with the broadcast the process exists for.
        if (Interlocked.Exchange(ref _isStarted, 1) != 0)
            return;

        new Thread(() => Initialize(context)) {
            Name = "firebase-init",
            IsBackground = true,
        }.Start();
    }

    private static void Initialize(Context context)
    {
        try {
            if (FirebaseApp.InitializeApp(context) is null)
                throw StandardError.Internal("FirebaseApp.InitializeApp returned null - no Firebase options resource");

            WhenReadySource.TrySetResult();
        }
        catch (Exception e) {
            // Logging isn't configured yet when this starts; logcat is what there is
            Android.Util.Log.Error(MauiDiagnostics.LogTag, $"Firebase init failed: {e}");
            WhenReadySource.TrySetException(e);
        }
    }
}
