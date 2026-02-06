using AndroidX.Core.View;

namespace ActualChat.App.Maui;

public partial class MainPage
{
    private partial void OnLoaded_Platform()
    {
        if (Handler?.PlatformView is Android.Views.View platformView)
            ViewCompat.SetOnApplyWindowInsetsListener(platformView, new CustomOnApplyWindowInsetsListener());
    }

    private class CustomOnApplyWindowInsetsListener : Java.Lang.Object, IOnApplyWindowInsetsListener
    {
        public WindowInsetsCompat? OnApplyWindowInsets(Android.Views.View? v, WindowInsetsCompat? insets)
        {
            var resultInsets = insets?.GetInsets(WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.Ime());
            if (resultInsets != null)
                v?.SetPadding(resultInsets.Left, resultInsets.Top, resultInsets.Right, resultInsets.Bottom);
            else
                v?.SetPadding(0, 0, 0, 0);

            return insets;
        }
    }
}
