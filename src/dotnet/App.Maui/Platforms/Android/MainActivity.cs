using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;

namespace ActualChat.App.Maui;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density )]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // TODO: move permissions request to where it's really needed
        // https://github.com/dotnet/maui/issues/3694#issuecomment-1014880727
        // https://stackoverflow.com/questions/70229906/blazor-maui-camera-and-microphone-android-permissions
        ActivityCompat.RequestPermissions(this, new[] {
            Manifest.Permission.Camera,
            Manifest.Permission.RecordAudio,
            Manifest.Permission.ModifyAudioSettings
        }, 0);
    }
}
