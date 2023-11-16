using Android.App;
using Android.Content;
using Android.Gms.Common.Util;
using Android.Gms.Common.Util.Concurrent;
using Android.Graphics;
using Android.Util;
using Firebase.Messaging;
using Java.Lang;
using Java.Util.Concurrent;
using JavaTimeoutException = Java.Util.Concurrent.TimeoutException;

namespace ActualChat.App.Maui;

public class FirebaseMessagingUtils(Context context)
{
    private const int ImageDownloadTimeoutSeconds = 5;
    private const string Tag = Firebase.Messaging.Constants.Tag;
    private const string ThreadNetworkIo = "Firebase-Messaging-Network-Io";

    private readonly Context _context = context ?? throw new ArgumentNullException(nameof(context));

    public bool IsAppForeground()
    {
        // port from https://github.com/firebase/firebase-android-sdk/blob/c51a44f6e55d88223c81d6cc5d9bfd11c05b530c/firebase-messaging/src/main/java/com/google/firebase/messaging/DisplayNotification.java#L60
        var keyguardManager = (KeyguardManager)_context.GetSystemService(Context.KeyguardService)!;
        if (keyguardManager.IsKeyguardLocked)
            return false; // Screen is off or lock screen is showing
        // Screen is on and unlocked, now check if the process is in the foreground

        if (!PlatformVersion.IsAtLeastLollipop) {
            // Before L the process has IMPORTANCE_FOREGROUND while it executes BroadcastReceivers.
            // As soon as the service is started the BroadcastReceiver should stop.
            // UNFORTUNATELY the system might not have had the time to downgrade the process
            // (this is happening consistently in JellyBean).
            // With SystemClock.sleep(10) we tell the system to give a little bit more of CPU
            // to the main thread (this code is executing on a secondary thread) allowing the
            // BroadcastReceiver to exit the onReceive() method and downgrade the process priority.
            Android.OS.SystemClock.Sleep(10);
        }
        int pid = Android.OS.Process.MyPid();
        ActivityManager am = (ActivityManager)_context.GetSystemService(Context.ActivityService)!;
        var appProcesses = am.RunningAppProcesses;
        if (appProcesses != null) {
            foreach (var process in appProcesses) {
                if (process.Pid == pid)
                    return process.Importance == Importance.Foreground;
            }
        }
        return false;
    }

    public static IExecutorService NewNetworkIoExecutor()
        => Executors.NewSingleThreadExecutor(new NamedThreadFactory(ThreadNetworkIo))!;

    public static ImageDownload? StartImageDownloadInBackground(Uri imageUrl)
    {
        // port from https://github.com/firebase/firebase-android-sdk/blob/c51a44f6e55d88223c81d6cc5d9bfd11c05b530c/firebase-messaging/src/main/java/com/google/firebase/messaging/DisplayNotification.java#L116
        var imageDownload = ImageDownload.Create(imageUrl.ToString());
        if (imageDownload != null) {
            var networkIoExecutor = NewNetworkIoExecutor();
            imageDownload.Start(networkIoExecutor);
        }
        return imageDownload;
    }

    public static Bitmap? WaitForAndApplyImageDownload(ImageDownload? imageDownload)
    {
        // port from https://github.com/firebase/firebase-android-sdk/blob/c51a44f6e55d88223c81d6cc5d9bfd11c05b530c/firebase-messaging/src/main/java/com/google/firebase/messaging/DisplayNotification.java#L125
        if (imageDownload == null)
            return null;

        /*
         * This blocks to wait for the image to finish downloading as this background thread is being
         * used to keep the app (via service or receiver) alive. It can't all be done on one thread
         * as the URLConnection API used to download the image is blocking, so another thread is needed
         * to enforce the timeout.
         */
        try {
            var result = Android.Gms.Tasks.TasksClass.Await(
                imageDownload.GetTask(),
                ImageDownloadTimeoutSeconds,
                TimeUnit.Seconds);
            return result as Bitmap;
        }
        catch (ExecutionException e) {
            // For all exceptions, fall through to show the notification without the image
            Log.Warn(Tag, "Failed to download image: " + e.Cause);
        }
        catch (InterruptedException) {
            Log.Warn(Tag, "Interrupted while downloading image, showing notification without it");
            imageDownload.Close();
            Java.Lang.Thread.CurrentThread().Interrupt(); // Restore the interrupted status
        }
        catch (JavaTimeoutException) {
            Log.Warn(Tag, "Failed to download image in time, showing notification without it");
            imageDownload.Close();
            /*
             * Instead of cancelling the task, could let the download continue, and update the
             * notification if it was still showing. For this we would need to cancel the download if the
             * user opens or dismisses the notification, and make sure the notification doesn't buzz again
             * when it is updated.
             */
        }
        return null;
    }
}
