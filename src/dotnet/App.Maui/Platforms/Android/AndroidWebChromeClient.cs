using Android;
using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Webkit;
using AndroidX.Activity;
using AndroidX.Activity.Result;
using AndroidX.Activity.Result.Contract;
using AndroidX.Core.Content;
using Java.Interop;
using JObject = Java.Lang.Object;
using View = Android.Views.View;
using WebView = Android.Webkit.WebView;

namespace ActualChat.App.Maui;

internal class AndroidWebChromeClient : WebChromeClient
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
        = "Actual Chat can use your camera to take and share pictures upon your request. "
        + "Please grant access to your camera when requested.";
    private const string LocationAccessRationale
        = "Actual Chat can share your location with your friends upon your request. "
        + "Please grant access to your precise location when requested.";
    private const string MicrophoneAccessRationale
        = "Actual Chat uses your microphone to record and transcribe your audio messages. "
        + "Please grant access to your microphone when requested.";

    private static readonly Dictionary<string, string> _rationalesByPermission = new(StringComparer.Ordinal) {
        [Manifest.Permission.Camera] = CameraAccessRationale,
        [Manifest.Permission.AccessFineLocation] = LocationAccessRationale,
        [Manifest.Permission.RecordAudio] = MicrophoneAccessRationale,
        // Add more rationales as you add more supported permissions.
    };

    private static readonly Dictionary<string, string[]> _requiredPermissionsByWebkitResource = new(StringComparer.Ordinal) {
        [PermissionRequest.ResourceVideoCapture] = new[] { Manifest.Permission.Camera },
        [PermissionRequest.ResourceAudioCapture] = new[] { Manifest.Permission.ModifyAudioSettings, Manifest.Permission.RecordAudio },
        // Add more Webkit resource -> Android permission mappings as needed.
    };

    private static ComponentActivity _activity = null!;
    private static ActivityResultLauncher _requestPermissionLauncher = null!;
    private static Action<bool>? _pendingPermissionRequestCallback;

    private readonly WebChromeClient _client;
    private readonly AndroidFileChooser _fileChooser;

    public AndroidWebChromeClient(WebChromeClient client, ComponentActivity activity, AndroidFileChooser fileChooser)
    {
        TryInitialize(activity);
        _client = client;
        _fileChooser = fileChooser;
    }

    public static void TryInitialize(ComponentActivity activity)
    {
        if (_activity == activity)
            return;

        _activity = activity;
        _requestPermissionLauncher = _activity.RegisterForActivityResult(
            new ActivityResultContracts.RequestPermission(),
            new ActivityResultCallback());
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

    private static void RequestAllResources(Memory<string> requestedResources, Action<List<string>> callback)
    {
        if (requestedResources.Length == 0) {
            // No resources to request - invoke the callback with an empty list.
            callback.Invoke(new());
            return;
        }

        var currentResource = requestedResources.Span[0];
        var requiredPermissions = _requiredPermissionsByWebkitResource.GetValueOrDefault(currentResource, Array.Empty<string>());

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
        => filePathCallback == null
            ? _client.OnShowFileChooser(webView, filePathCallback, fileChooserParams)
            : _fileChooser.OnShowFileChooser(_activity, filePathCallback);

    #region Unremarkable overrides
    // See: https://github.com/dotnet/maui/issues/6565
    public override JniPeerMembers JniPeerMembers => _client.JniPeerMembers;
    public override Bitmap? DefaultVideoPoster => _client.DefaultVideoPoster;
    public override View? VideoLoadingProgressView => _client.VideoLoadingProgressView;
    public override void GetVisitedHistory(IValueCallback? callback)
        => _client.GetVisitedHistory(callback);
    public override bool OnConsoleMessage(ConsoleMessage? consoleMessage)
        => _client.OnConsoleMessage(consoleMessage);
    public override bool OnCreateWindow(WebView? view, bool isDialog, bool isUserGesture, Message? resultMsg)
        => _client.OnCreateWindow(view, isDialog, isUserGesture, resultMsg);
    public override void OnCloseWindow(WebView? window)
        => _client.OnCloseWindow(window);
    public override void OnGeolocationPermissionsHidePrompt()
        => _client.OnGeolocationPermissionsHidePrompt();
    public override void OnHideCustomView()
        => _client.OnHideCustomView();
    public override bool OnJsAlert(WebView? view, string? url, string? message, JsResult? result)
        => _client.OnJsAlert(view, url, message, result);
    public override bool OnJsBeforeUnload(WebView? view, string? url, string? message, JsResult? result)
        => _client.OnJsBeforeUnload(view, url, message, result);
    public override bool OnJsConfirm(WebView? view, string? url, string? message, JsResult? result)
        => _client.OnJsConfirm(view, url, message, result);
    public override bool OnJsPrompt(WebView? view, string? url, string? message, string? defaultValue, JsPromptResult? result)
        => _client.OnJsPrompt(view, url, message, defaultValue, result);
    public override void OnPermissionRequestCanceled(PermissionRequest? request)
        => _client.OnPermissionRequestCanceled(request);
    public override void OnProgressChanged(WebView? view, int newProgress)
        => _client.OnProgressChanged(view, newProgress);
    public override void OnReceivedIcon(WebView? view, Bitmap? icon)
        => _client.OnReceivedIcon(view, icon);
    public override void OnReceivedTitle(WebView? view, string? title)
        => _client.OnReceivedTitle(view, title);
    public override void OnReceivedTouchIconUrl(WebView? view, string? url, bool precomposed)
        => _client.OnReceivedTouchIconUrl(view, url, precomposed);
    public override void OnRequestFocus(WebView? view)
        => _client.OnRequestFocus(view);
    public override void OnShowCustomView(View? view, ICustomViewCallback? callback)
        => _client.OnShowCustomView(view, callback);
    #endregion

    // Private methods

    private static void RequestAllPermissions(Memory<string> requiredPermissions, Action<bool> callback)
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

    private static void RequestPermission(string permission, Action<bool> callback)
    {
        // This method implements the workflow described here:
        // https://developer.android.com/training/permissions/requesting#workflow_for_requesting_permissions

        if (ContextCompat.CheckSelfPermission(_activity, permission) == Permission.Granted) {
            callback.Invoke(true);
        }
        else if (_activity.ShouldShowRequestPermissionRationale(permission) && _rationalesByPermission.TryGetValue(permission, out var rationale)) {
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

    private static void LaunchPermissionRequestActivity(string permission, Action<bool> callback)
    {
        if (_pendingPermissionRequestCallback is not null)
            throw StandardError.Constraint("Cannot perform multiple permission requests simultaneously.");

        _pendingPermissionRequestCallback = callback;
        _requestPermissionLauncher.Launch(permission);
    }

    // Nested types

    public sealed class ActivityResultCallback : JObject, IActivityResultCallback
    {
        public void OnActivityResult(JObject? isGranted)
        {
            var callback = _pendingPermissionRequestCallback;
            _pendingPermissionRequestCallback = null;
            callback?.Invoke(isGranted != null && (bool)isGranted);
        }
    }
}
