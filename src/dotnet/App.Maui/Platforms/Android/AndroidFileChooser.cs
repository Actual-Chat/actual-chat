using Android.Content;
using Android.OS;
using Android.Webkit;
using Android.Provider;
using Activity = Android.App.Activity;
using Result = Android.App.Result;
using Uri = Android.Net.Uri;
using File = Java.IO.File;
using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;
using ComponentActivity = AndroidX.Activity.ComponentActivity;

namespace ActualChat.App.Maui;

internal class AndroidFileChooser
{
    private const int InputFileRequestCode = 1000;

    private File? _cameraPhotoPath;
    private File? _cameraVideoPath;
    private IValueCallback? _filePathCallback;

    private ILogger Log { get; }

    public AndroidFileChooser(ILogger log)
    {
        Log = log;
        AndroidActivityResultHandlers.Register(OnActivityResult);
    }

    public bool OnShowFileChooser(
        ComponentActivity activity,
        IValueCallback filePathCallback)
    {
        _filePathCallback?.OnReceiveValue(null);
        _filePathCallback = filePathCallback;
        _ = ShowChooser(activity);
        return true;
    }

    private async Task ShowChooser(ComponentActivity activity)
    {
        var initialIntents = new List<Intent>();

        if (MediaPicker.Default.IsCaptureSupported) {
            try {
                await PermissionsEx.EnsureGrantedAsync<MauiPermissions.Camera>().ConfigureAwait(true);
                // StorageWrite no longer exists starting from Android API 33
                if (!OperatingSystem.IsAndroidVersionAtLeast(33))
                    await PermissionsEx.EnsureGrantedAsync<MauiPermissions.StorageWrite>().ConfigureAwait(true);
                else
                    await PermissionsEx.EnsureGrantedAsync<MauiPermissions.Media>().ConfigureAwait(true);

                var takePictureIntent = CreateCaptureIntent(activity, true);
                if (takePictureIntent != null)
                    initialIntents.Add(takePictureIntent);
                var takeVideoIntent = CreateCaptureIntent(activity, false);
                if (takeVideoIntent != null)
                    initialIntents.Add(takeVideoIntent);
            }
            catch (PermissionException e) {
                Log.LogWarning(e, "Failed to add capture camera intents");
            }
        }

        Intent contentSelectionIntent = new Intent(Intent.ActionGetContent);
        contentSelectionIntent.AddCategory(Intent.CategoryOpenable);
        contentSelectionIntent.PutExtra(Intent.ExtraAllowMultiple, true);
        contentSelectionIntent.SetType("*/*");

        var chooserIntent = Intent.CreateChooser(contentSelectionIntent, "")!;
        chooserIntent.PutExtra(Intent.ExtraIntent, contentSelectionIntent);
        chooserIntent.PutExtra(Intent.ExtraInitialIntents, initialIntents.ToArray<IParcelable>());
        activity.StartActivityForResult(chooserIntent, InputFileRequestCode);
    }

    private Intent? CreateCaptureIntent(Context context, bool isPhoto)
    {
        // A copy from https://github.com/dotnet/maui/blob/main/src/Essentials/src/MediaPicker/MediaPicker.android.cs#L70
        // We need to copy because we need to pass capture intent to Chooser Intent as extra parameters.

        var captureIntent = new Intent(isPhoto ? MediaStore.ActionImageCapture : MediaStore.ActionVideoCapture);

        if (!IsIntentSupported(captureIntent)) {
            Log.LogWarning("Either there was no camera on the device or '{IntentAction}' was not added to the <queries> element in the app's manifest file. See more: https://developer.android.com/about/versions/11/privacy/package-visibility", captureIntent.Action);
            return null;
        }

        // Create the temporary file
        var prefix = isPhoto ? "IMG_" : "VID_";
        var ext = isPhoto ? ".jpg" : ".mp4";
        var timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var tmpFileName = prefix + timeStamp + ext;
        var tmpFile = FileSystemUtilsEx.GetTemporaryFile(context.CacheDir!, tmpFileName);

        var fileUri = FileProviderEx.GetUriForFile(tmpFile);
        captureIntent.PutExtra(MediaStore.ExtraOutput, fileUri);
        if (isPhoto)
            _cameraPhotoPath = tmpFile;
        else
            _cameraVideoPath = tmpFile;

        captureIntent.AddFlags(ActivityFlags.GrantReadUriPermission);
        captureIntent.AddFlags(ActivityFlags.GrantWriteUriPermission);

        return captureIntent;
    }

    private static bool IsIntentSupported(Intent intent)
    {
        if (Platform.AppContext is not { PackageManager: { } pm })
            return false;
        return intent.ResolveActivity(pm) is not null;
    }

    private void OnActivityResult(Activity activity, int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != InputFileRequestCode || _filePathCallback == null)
            return;

        _ = Proceed();

        async Task Proceed()
        {
            Uri[]? results = null;

            if (resultCode == Result.Ok) {
                if (data is { ClipData.ItemCount: > 0 }) {
                    results = new Uri[data.ClipData.ItemCount];
                    for (int i = 0; i < data.ClipData.ItemCount; i++)
                        results[i] = data.ClipData.GetItemAt(i)!.Uri!;
                }
                else if (data is { DataString: not null }) {
                    results = new Uri[] { Uri.Parse(data.DataString)! };
                }
                else {
                    if (_cameraPhotoPath != null && _cameraPhotoPath.Length() > 0)
                        results = await LoadCapturedFileAsync(new FileResult(_cameraPhotoPath.AbsolutePath)).ConfigureAwait(true);
                    else if (_cameraVideoPath != null && _cameraVideoPath.Length() > 0)
                        results = await LoadCapturedFileAsync(new FileResult(_cameraVideoPath.AbsolutePath)).ConfigureAwait(true);
                }
            }

            _filePathCallback.OnReceiveValue(results);
            _filePathCallback = null;
            _cameraPhotoPath = null;
            _cameraVideoPath = null;
        }
    }

    private async Task<Uri[]?> LoadCapturedFileAsync(FileResult? fileResult)
    {
        if (fileResult == null)
            return null;

        // save the file into local storage
        var newFile = Path.Combine(FileSystem.CacheDirectory, fileResult.FileName);
        using (var stream = await fileResult.OpenReadAsync().ConfigureAwait(false))
        using (var newStream = System.IO.File.OpenWrite(newFile))
            await stream.CopyToAsync(newStream).ConfigureAwait(true);
        var uri = Uri.FromFile(new File(newFile));
        return new Uri[] { uri! };
    }
}
