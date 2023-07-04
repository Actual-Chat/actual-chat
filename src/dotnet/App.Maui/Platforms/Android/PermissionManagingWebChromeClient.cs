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
using View = Android.Views.View;
using WebView = Android.Webkit.WebView;

namespace ActualChat.App.Maui;

internal class PermissionManagingWebChromeClient : WebChromeClient, IActivityResultCallback
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
    private const string CameraAccessRationale = "This app requires access to your camera. Please grant access to your camera when requested.";
    private const string LocationAccessRationale = "This app requires access to your location. Please grant access to your precise location when requested.";
    private const string MicrophoneAccessRationale = "This app requires access to your microphone. Please grant access to your microphone when requested.";

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

    private readonly WebChromeClient _webChromeClient;
    private readonly ComponentActivity _activity;
    private readonly ActivityResultLauncher _requestPermissionLauncher;

    private Action<bool>? _pendingPermissionRequestCallback;

    public PermissionManagingWebChromeClient(WebChromeClient webChromeClient, ComponentActivity activity)
    {
        _webChromeClient = webChromeClient;
        _activity = activity;
        _requestPermissionLauncher = _activity.RegisterForActivityResult(new ActivityResultContracts.RequestPermission(), this);
    }

    public override void OnCloseWindow(WebView? window)
    {
        _webChromeClient.OnCloseWindow(window);
        _requestPermissionLauncher.Unregister();
    }

    public override void OnGeolocationPermissionsShowPrompt(string? origin, GeolocationPermissions.ICallback? callback)
    {
        ArgumentNullException.ThrowIfNull(callback, nameof(callback));

        RequestPermission(Manifest.Permission.AccessFineLocation, isGranted => callback.Invoke(origin, isGranted, false));
    }

    public override void OnPermissionRequest(PermissionRequest? request)
    {
        ArgumentNullException.ThrowIfNull(request, nameof(request));

        if (request.GetResources() is not { } requestedResources) {
            request.Deny();
            return;
        }

        RequestAllResources(requestedResources, grantedResources => {
            if (grantedResources.Count == 0) {
                request.Deny();
            }
            else {
                request.Grant(grantedResources.ToArray());
            }
        });
    }

    private void RequestAllResources(Memory<string> requestedResources, Action<List<string>> callback)
    {
        if (requestedResources.Length == 0) {
            // No resources to request - invoke the callback with an empty list.
            callback(new());
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
        else if (_activity.ShouldShowRequestPermissionRationale(permission) && _rationalesByPermission.TryGetValue(permission, out var rationale)) {
            new AlertDialog.Builder(_activity)
                .SetTitle("Enable app permissions")!
                .SetMessage(rationale)!
                .SetNegativeButton("No thanks", (_, _) => callback(false))!
                .SetPositiveButton("Continue", (_, _) => LaunchPermissionRequestActivity(permission, callback))!
                .Show();
        }
        else {
            LaunchPermissionRequestActivity(permission, callback);
        }
    }

    private void LaunchPermissionRequestActivity(string permission, Action<bool> callback)
    {
        if (_pendingPermissionRequestCallback is not null)
            throw StandardError.Constraint("Cannot perform multiple permission requests simultaneously.");

        _pendingPermissionRequestCallback = callback;
        _requestPermissionLauncher.Launch(permission);
    }

    void IActivityResultCallback.OnActivityResult(Java.Lang.Object? isGranted)
    {
        var callback = _pendingPermissionRequestCallback;
        _pendingPermissionRequestCallback = null;
        callback?.Invoke(isGranted != null && (bool)isGranted);
    }

    #region Unremarkable overrides
    // See: https://github.com/dotnet/maui/issues/6565
    public override JniPeerMembers JniPeerMembers => _webChromeClient.JniPeerMembers;
    public override Bitmap? DefaultVideoPoster => _webChromeClient.DefaultVideoPoster;
    public override View? VideoLoadingProgressView => _webChromeClient.VideoLoadingProgressView;
    public override void GetVisitedHistory(IValueCallback? callback)
        => _webChromeClient.GetVisitedHistory(callback);
    public override bool OnConsoleMessage(ConsoleMessage? consoleMessage)
        => _webChromeClient.OnConsoleMessage(consoleMessage);
    public override bool OnCreateWindow(WebView? view, bool isDialog, bool isUserGesture, Message? resultMsg)
        => _webChromeClient.OnCreateWindow(view, isDialog, isUserGesture, resultMsg);
    public override void OnGeolocationPermissionsHidePrompt()
        => _webChromeClient.OnGeolocationPermissionsHidePrompt();
    public override void OnHideCustomView()
        => _webChromeClient.OnHideCustomView();
    public override bool OnJsAlert(WebView? view, string? url, string? message, JsResult? result)
        => _webChromeClient.OnJsAlert(view, url, message, result);
    public override bool OnJsBeforeUnload(WebView? view, string? url, string? message, JsResult? result)
        => _webChromeClient.OnJsBeforeUnload(view, url, message, result);
    public override bool OnJsConfirm(WebView? view, string? url, string? message, JsResult? result)
        => _webChromeClient.OnJsConfirm(view, url, message, result);
    public override bool OnJsPrompt(WebView? view, string? url, string? message, string? defaultValue, JsPromptResult? result)
        => _webChromeClient.OnJsPrompt(view, url, message, defaultValue, result);
    public override void OnPermissionRequestCanceled(PermissionRequest? request)
        => _webChromeClient.OnPermissionRequestCanceled(request);
    public override void OnProgressChanged(WebView? view, int newProgress)
        => _webChromeClient.OnProgressChanged(view, newProgress);
    public override void OnReceivedIcon(WebView? view, Bitmap? icon)
        => _webChromeClient.OnReceivedIcon(view, icon);
    public override void OnReceivedTitle(WebView? view, string? title)
        => _webChromeClient.OnReceivedTitle(view, title);
    public override void OnReceivedTouchIconUrl(WebView? view, string? url, bool precomposed)
        => _webChromeClient.OnReceivedTouchIconUrl(view, url, precomposed);
    public override void OnRequestFocus(WebView? view)
        => _webChromeClient.OnRequestFocus(view);
    public override void OnShowCustomView(View? view, ICustomViewCallback? callback)
        => _webChromeClient.OnShowCustomView(view, callback);
    public override bool OnShowFileChooser(WebView? webView, IValueCallback? filePathCallback, FileChooserParams? fileChooserParams)
        => _webChromeClient.OnShowFileChooser(webView, filePathCallback, fileChooserParams);
    #endregion
}
