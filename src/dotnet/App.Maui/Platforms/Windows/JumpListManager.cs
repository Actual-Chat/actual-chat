using Windows.UI.StartScreen;
using Windows.UI.ViewManagement;

namespace ActualChat.App.Maui;

public static partial class JumpListManager
{
    public const string QuitArgs = "--quit";
    private const int AppModelErrorNoPackage = 15700;
    // 48px PNGs, not the jump_item_quit.svg they're rendered from: the shell decodes jump list logos
    // through WIC, which has no SVG decoder. One size rather than scale-NNN variants because
    // AppxDefaultResourceQualifiers pins Scale=200, so the package would drop every other one.
    private const string DarkLogoUrl = "ms-appx:///Platforms/Windows/Assets/jump_item_quit_dark.png";
    private const string LightLogoUrl = "ms-appx:///Platforms/Windows/Assets/jump_item_quit_light.png";

    private static readonly bool IsPackaged = GetIsPackaged();
    private static readonly UISettings SystemUISettings = new();
    private static int _isThemeTracked;

    public static async Task PopulateJumpList()
    {
        // The WinRT jump list saves fine unpackaged, but its items carry only Arguments - the shell
        // resolves what to launch from the AppUserModelID, which an unpackaged app doesn't have. The
        // item would draw and do nothing, so drop it, including one an earlier build left behind.
        if (!IsPackaged) {
            await ClearJumpList();
            return;
        }

        TrackTheme();
        var jumpList = await JumpList.LoadCurrentAsync();
        if (!UpdateQuitItem(jumpList))
            return;

        await jumpList.SaveAsync();
    }

    public static async Task ClearJumpList()
    {
        var jumpList = await JumpList.LoadCurrentAsync();
        var quitItem = jumpList.Items.FirstOrDefault(c => c.Arguments == QuitArgs);
        if (quitItem == null)
            return;

        jumpList.Items.Remove(quitItem);
        await jumpList.SaveAsync();
    }

    // Private methods

    private static void TrackTheme()
    {
        // SystemUISettings has to be a static field: ColorValuesChanged stops firing once the
        // UISettings instance it was subscribed on is collected.
        if (Interlocked.Exchange(ref _isThemeTracked, 1) != 0)
            return;

        SystemUISettings.ColorValuesChanged += (_, _) => _ = PopulateJumpList();
    }

    private static bool UpdateQuitItem(JumpList jumpList)
    {
        var logo = GetLogoUri();
        var item = jumpList.Items.FirstOrDefault(c => c.Arguments == QuitArgs);
        if (item != null && item.Logo == logo)
            return false;

        if (item == null) {
            item = JumpListItem.CreateWithArguments(QuitArgs, $"Quit {CoreConstants.AppName}");
            jumpList.Items.Add(item);
        }
        item.Logo = logo;
        return true;
    }

    private static Uri GetLogoUri()
    {
        // The flyout follows the Windows theme rather than ours, and a light foreground means a dark
        // theme - so that's when the glyph must be the light one.
        var foreground = SystemUISettings.GetColorValue(UIColorType.Foreground);
        var brightness = (0.299 * foreground.R + 0.587 * foreground.G + 0.114 * foreground.B) / 255;
        return (brightness >= 0.5 ? LightLogoUrl : DarkLogoUrl).ToUri();
    }

    private static bool GetIsPackaged()
    {
        // A zero-length buffer makes this fail either way - what tells the two apart is how.
        var length = 0;
        return GetCurrentPackageFullName(ref length, 0) != AppModelErrorNoPackage;
    }

    [LibraryImport("kernel32.dll")]
    private static partial int GetCurrentPackageFullName(ref int packageFullNameLength, nint packageFullName);
}
