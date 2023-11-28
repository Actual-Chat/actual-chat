namespace ActualChat.App.Maui;

public class AndroidBars : Bars
{
    public override int GetStatusBarHeight()
    {
        var resources = MainActivity.Current.Resources;
        if (resources is null)
            return 0;

        var resourceId = resources.GetIdentifier("status_bar_height", "dimen", "android");
        if (resourceId <= 0)
            return 0;

        var height = (int)(resources.GetDimensionPixelSize(resourceId) / resources.DisplayMetrics?.Density ?? 1);
        return height;
    }
}
