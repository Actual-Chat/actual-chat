using AVFoundation;
using Contacts;
using CoreLocation;

namespace ActualChat.App.Maui;

/// <summary>
/// The TCC calls behind the AppKit permission handlers - what MAUI Essentials' Permissions wraps
/// on iOS and the labs package leaves unimplemented. Null means the user hasn't been asked yet.
/// </summary>
public static class MacOSPermissions
{
    private static CLLocationManager? _locationManager;

    public static bool? IsMediaCaptureGranted(AVAuthorizationMediaType mediaType)
        => AVCaptureDevice.GetAuthorizationStatus(mediaType) switch {
            AVAuthorizationStatus.NotDetermined => null,
            AVAuthorizationStatus.Authorized => true,
            _ => false,
        };

    public static Task<bool> RequestMediaCapture(AVAuthorizationMediaType mediaType)
        => AVCaptureDevice.RequestAccessForMediaTypeAsync(mediaType);

    public static bool? IsContactsGranted()
        => CNContactStore.GetAuthorizationStatus(CNEntityType.Contacts) switch {
            CNAuthorizationStatus.NotDetermined => null,
            CNAuthorizationStatus.Authorized => true,
            CNAuthorizationStatus.Limited => true,
            _ => false,
        };

    public static async Task<bool> RequestContacts()
    {
        using var store = new CNContactStore();
        var (isGranted, _) = await store.RequestAccessAsync(CNEntityType.Contacts).ConfigureAwait(false);
        return isGranted;
    }

    public static Task<bool?> IsLocationGranted()
        => MacOSMainThread.InvokeOnMainThreadAsync(() => ToIsGranted(LocationManager.AuthorizationStatus));

    public static Task<bool> RequestLocation(CancellationToken cancellationToken)
        => MacOSMainThread.InvokeOnMainThreadAsync(async () => {
            var manager = LocationManager;
            if (ToIsGranted(manager.AuthorizationStatus) is { } isGranted)
                return isGranted;

            // The prompt's outcome arrives through the delegate, never as a return value
            var whenDecided = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.AuthorizationChanged += OnAuthorizationChanged;
            try {
                manager.RequestWhenInUseAuthorization();
                return await whenDecided.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
            }
            finally {
                manager.AuthorizationChanged -= OnAuthorizationChanged;
            }

            void OnAuthorizationChanged(object? sender, CLAuthorizationChangedEventArgs e) {
                if (ToIsGranted(e.Status) is { } isDecided)
                    whenDecided.TrySetResult(isDecided);
            }
        });

    // Private methods

    // CLLocationManager wants a thread with a run loop, so every access to it goes through the main thread
    private static CLLocationManager LocationManager => _locationManager ??= new CLLocationManager();

    private static bool? ToIsGranted(CLAuthorizationStatus status)
        => status switch {
            CLAuthorizationStatus.NotDetermined => null,
            CLAuthorizationStatus.Authorized => true,
            CLAuthorizationStatus.AuthorizedWhenInUse => true,
            _ => false,
        };
}
