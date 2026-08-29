using Android;
using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Webkit;
using AndroidX.Core.Content;
using Java.Interop;
using View = Android.Views.View;
using WebView = Android.Webkit.WebView;

namespace ActualChat.App.Maui;

// Extends https://github.com/dotnet/maui/blob/main/src/BlazorWebView/src/Maui/Android/BlazorWebChromeClient.cs
public class AndroidWebChromeClient : WebChromeClient
{
    // Example to control permissions in browser is taken from the comment
    // https://github.com/dotnet/maui/issues/4768#issuecomment-1137906982
    // https://github.com/MackinnonBuck/MauiBlazorPermissionsExample

    // This class implements a permission requesting workflow that matches workflow recommended
    // by the official Android developer documentation.
    // See: https://developer.android.com/training/permissions/requesting#workflow_for_requesting_permissions
    // The current implementation supports location, camera, and microphone permissions. To add your own,
    // update the s_rationalesByPermission dictionary to include your rationale for requiring the permission.
    // If necessary, you may need to also update s_requiredPermissionsByWebkitResource to define how a specific
    // Webkit resource maps to an Android permission.

    // In a real app, you would probably use more convincing rationales tailored toward what your app does.
    private const string CameraAccessRationale
        = $"{CoreConstants.AppName} can use your camera to take and share pictures upon your request. "
        + "Please grant access to your camera when requested.";
    private const string LocationAccessRationale
        = $"{CoreConstants.AppName} can share your location with your friends upon your request. "
        + "Please grant access to your precise location when requested.";
    private const string MicrophoneAccessRationale
        = $"{CoreConstants.AppName} uses your microphone to record and transcribe your audio messages. "
        + "Please grant access to your microphone when requested.";

    private static readonly Dictionary<string, string> RationalesByPermission = new() {
        [Manifest.Permission.Camera] = CameraAccessRationale,
        [Manifest.Permission.AccessFineLocation] = LocationAccessRationale,
        [Manifest.Permission.RecordAudio] = MicrophoneAccessRationale,
        // Add more rationales as you add more supported permissions.
    };

    private static readonly Dictionary<string, string[]> RequiredPermissionsByWebkitResource = new() {
        [PermissionRequest.ResourceVideoCapture] = new[] { Manifest.Permission.Camera },
        [PermissionRequest.ResourceAudioCapture] = new[] { Manifest.Permission.ModifyAudioSettings, Manifest.Permission.RecordAudio },
        // Add more Webkit resource -> Android permission mappings as needed.
    };

    private readonly MainActivity _activity;
    private readonly WebChromeClient _client;
    private readonly VisualMediaFileChooser _visualMediaFileChooser;
    private ILogger Log { get; }
    private bool IsDisconnected { get; set; }
    private bool CanForward
        // MAUI disposes the wrapped client on disconnect, but this wrapper lives on until Destroy().
        => !IsDisconnected && _client.IsValid();

    // ReSharper disable once ConvertToPrimaryConstructor
    public AndroidWebChromeClient(
        WebChromeClient client,
        MainActivity activity,
        VisualMediaFileChooser visualMediaFileChooser,
        ILogger<AndroidWebChromeClient> log)
    {
        _activity = activity;
        _client = client;
        _visualMediaFileChooser = visualMediaFileChooser;
        Log = log;
    }

    public void MarkDisconnected()
        => IsDisconnected = true;

    protected override void Dispose(bool disposing)
    {
        Log.LogDebug("Dispose. Disposing={Disposing}", disposing);
        if (disposing && _client.IsValid())
            _client.Dispose();
        base.Dispose(disposing);
    }

    public override void OnGeolocationPermissionsShowPrompt(string? origin, GeolocationPermissions.ICallback? callback)
        => RequestPermission(
            Manifest.Permission.AccessFineLocation,
            isGranted => callback?.Invoke(origin, isGranted, false));

    public override void OnPermissionRequest(PermissionRequest? request)
    {
        if (request == null)
            return;

        if (request.GetResources() is not { } requestedResources) {
            request.Deny();
            return;
        }

        RequestAllResources(requestedResources, grantedResources => {
            if (grantedResources.Count == 0)
                request.Deny();
            else
                request.Grant(grantedResources.ToArray());
        });
    }

    // Private methods

    private void RequestAllResources(Memory<string> requestedResources, Action<List<string>> callback)
    {
        if (requestedResources.Length == 0) {
            // No resources to request - invoke the callback with an empty list.
            callback.Invoke([]);
            return;
        }

        var currentResource = requestedResources.Span[0];
        var requiredPermissions = RequiredPermissionsByWebkitResource.GetValueOrDefault(currentResource, []);

        RequestAllPermissions(requiredPermissions, isGranted => {
            // Recurse with the remaining resources. If the first resource was granted, use a modified callback
            // that adds the first resource to the granted resources list.
            RequestAllResources(requestedResources[1..], !isGranted ? callback : grantedResources => {
                grantedResources.Add(currentResource);
                callback(grantedResources);
            });
        });
    }

    public override bool OnShowFileChooser(
        WebView? webView,
        IValueCallback? filePathCallback,
        FileChooserParams? fileChooserParams)
    {
        if (!CanForward)
            return false;

        var acceptTypes = fileChooserParams?.GetAcceptTypes() ?? [];
        if (filePathCallback is not null
            && acceptTypes.Length > 0
            && _visualMediaFileChooser.OnShowFileChooser(acceptTypes[0], uris => {
                filePathCallback.OnReceiveValue(uris);
            }))
            return true;

        return _client.OnShowFileChooser(webView, filePathCallback, fileChooserParams);
    }

    #region Unremarkable overrides
    // See: https://github.com/dotnet/maui/issues/6565
    public override JniPeerMembers JniPeerMembers => _client.JniPeerMembers;
    public override Bitmap? DefaultVideoPoster => _client.DefaultVideoPoster;
    public override View? VideoLoadingProgressView => _client.VideoLoadingProgressView;
    public override void GetVisitedHistory(IValueCallback? callback)
    {
        if (CanForward)
            _client.GetVisitedHistory(callback);
    }

    // Not overridden in prod: every JS console message crosses JNI into managed code on the UI thread
    // and allocates a ConsoleMessage peer holding a JNI global ref. Once the process nears the 51200-ref
    // cap, .NET for Android forces a full GC per new ref, which parks the UI thread and ANRs the app.
#if !IS_PRODUCTION_ENV || DEBUG
    public override bool OnConsoleMessage(ConsoleMessage? consoleMessage)
    {
        if (IsDisconnected)
            return false;

        return _client.OnConsoleMessage(consoleMessage);
    }
#endif

    public override bool OnCreateWindow(WebView? view, bool isDialog, bool isUserGesture, Message? resultMsg)
    {
        try {
            return _client.OnCreateWindow(view, isDialog, isUserGesture, resultMsg);
        }
        catch (Exception e) {
            // A target=_blank / window.open() whose URL has no handling activity makes the default
            // client throw (e.g. ActivityNotFoundException) on the UI thread, crashing the app.
            // Decline the new window instead.
            Log.LogWarning(e, "OnCreateWindow failed");
            return false;
        }
    }

    public override void OnCloseWindow(WebView? window)
    {
        if (CanForward)
            _client.OnCloseWindow(window);
    }

    public override void OnGeolocationPermissionsHidePrompt()
    {
        if (CanForward)
            _client.OnGeolocationPermissionsHidePrompt();
    }

    public override bool OnJsAlert(WebView? view, string? url, string? message, JsResult? result)
        => CanForward && _client.OnJsAlert(view, url, message, result);
    public override bool OnJsBeforeUnload(WebView? view, string? url, string? message, JsResult? result)
        => CanForward && _client.OnJsBeforeUnload(view, url, message, result);
    public override bool OnJsConfirm(WebView? view, string? url, string? message, JsResult? result)
        => CanForward && _client.OnJsConfirm(view, url, message, result);
    public override bool OnJsPrompt(WebView? view, string? url, string? message, string? defaultValue, JsPromptResult? result)
        => CanForward && _client.OnJsPrompt(view, url, message, defaultValue, result);

    public override void OnPermissionRequestCanceled(PermissionRequest? request)
    {
        if (CanForward)
            _client.OnPermissionRequestCanceled(request);
    }

    public override void OnProgressChanged(WebView? view, int newProgress)
    {
        if (IsDisconnected)
            return;

        _client.OnProgressChanged(view, newProgress);
    }

    public override void OnReceivedIcon(WebView? view, Bitmap? icon)
    {
        if (IsDisconnected)
            return;

        _client.OnReceivedIcon(view, icon);
    }

    public override void OnReceivedTitle(WebView? view, string? title)
    {
        if (IsDisconnected)
            return;

        _client.OnReceivedTitle(view, title);
    }

    public override void OnReceivedTouchIconUrl(WebView? view, string? url, bool precomposed)
    {
        if (IsDisconnected)
            return;

        _client.OnReceivedTouchIconUrl(view, url, precomposed);
    }

    public override void OnRequestFocus(WebView? view)
    {
        if (CanForward)
            _client.OnRequestFocus(view);
    }
    #endregion

    #region Excluded Unremarkable overrides
    // Do not override OnShowCustomView and OnHideCustomView.
    // When they are overriden WebView suppose that it supports fullscreen video playback
    // but in fact it can not do it.
    // public override void OnShowCustomView(View? view, ICustomViewCallback? callback)
    //     => _client.OnShowCustomView(view, callback);
    // public override void OnHideCustomView()
    //     => _client.OnHideCustomView();
    #endregion

    // Private methods

    private void RequestAllPermissions(Memory<string> requiredPermissions, Action<bool> callback)
    {
        if (requiredPermissions.Length == 0) {
            // No permissions left to request - success!
            callback(true);
            return;
        }

        RequestPermission(requiredPermissions.Span[0], isGranted => {
            if (isGranted) {
                // Recurse with the remaining permissions.
                RequestAllPermissions(requiredPermissions[1..], callback);
            }
            else {
                // The first required permission was not granted. Fail now and don't attempt to grant
                // the remaining permissions.
                callback(false);
            }
        });
    }

    private void RequestPermission(string permission, Action<bool> callback)
    {
        // This method implements the workflow described here:
        // https://developer.android.com/training/permissions/requesting#workflow_for_requesting_permissions

        if (ContextCompat.CheckSelfPermission(_activity, permission) == Permission.Granted) {
            callback.Invoke(true);
        }
        else if (_activity.ShouldShowRequestPermissionRationale(permission) && RationalesByPermission.TryGetValue(permission, out var rationale)) {
            new AlertDialog.Builder(_activity)
                .SetTitle("Enable app permissions")!
                .SetMessage(rationale)!
                .SetNegativeButton("No thanks", (_, _) => callback.Invoke(false))!
                .SetPositiveButton("Continue", (_, _) => LaunchPermissionRequestActivity(permission, callback))!
                .Show();
        }
        else {
            LaunchPermissionRequestActivity(permission, callback);
        }
    }

    private void LaunchPermissionRequestActivity(string permission, Action<bool> callback)
        => _activity.RequestPermission(permission, callback, true);
}
