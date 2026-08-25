using ActualChat.UI;

namespace ActualChat.Maui;

/// <summary>
/// The app's design tokens (see src/nodejs/styles/colors.css) as UIKit colors:
/// the raw ramp first, then the roles, which resolve per <see cref="Theme"/>.
/// </summary>
public static class AppColors
{
    // Ramp
    public static readonly UIColor Black = UIColor.FromRGB(0x00, 0x00, 0x00);
    public static readonly UIColor White = UIColor.FromRGB(0xFF, 0xFF, 0xFF);
    public static readonly UIColor Gray05 = UIColor.FromRGB(0x1C, 0x1C, 0x1C);
    public static readonly UIColor Gray40 = UIColor.FromRGB(0x77, 0x77, 0x77);
    public static readonly UIColor Gray60 = UIColor.FromRGB(0xA0, 0xA0, 0xA0);
    public static readonly UIColor Gray95 = UIColor.FromRGB(0xF3, 0xF3, 0xF3);
    public static readonly UIColor Ash10 = UIColor.FromRGB(0x16, 0x16, 0x1A);
    public static readonly UIColor Ash18 = UIColor.FromRGB(0x28, 0x28, 0x2E);
    public static readonly UIColor Ash26 = UIColor.FromRGB(0x3D, 0x3D, 0x47);
    public static readonly UIColor Ash30 = UIColor.FromRGB(0x42, 0x42, 0x4D);
    public static readonly UIColor Ash40 = UIColor.FromRGB(0x57, 0x57, 0x66);
    public static readonly UIColor Ash50 = UIColor.FromRGB(0x6D, 0x6D, 0x80);
    public static readonly UIColor Ash70 = UIColor.FromRGB(0x9B, 0x9B, 0xB2);
    public static readonly UIColor Ash90 = UIColor.FromRGB(0xCF, 0xCF, 0xE5);
    public static readonly UIColor Ash99 = UIColor.FromRGB(0xF5, 0xF5, 0xFC);
    public static readonly UIColor Blue60 = UIColor.FromRGB(0x33, 0x95, 0xFF);
    public static readonly UIColor Blue70 = UIColor.FromRGB(0x54, 0xA6, 0xFF);
    // Roles
    public static readonly UIColor Background01 = Dynamic(White, Ash18);
    public static readonly UIColor Text01 = Dynamic(Gray05, Ash10, Ash99);
    public static readonly UIColor Text03 = Dynamic(Gray40, Ash40, Ash70);
    public static readonly UIColor Text04 = Dynamic(Gray60, Ash50, Ash50);
    public static readonly UIColor Primary = Dynamic(Blue60, Blue70);
    public static readonly UIColor PrimaryTitle = White;
    public static readonly UIColor Input = Dynamic(Black.ColorWithAlpha(0.05f), Ash99.ColorWithAlpha(0.05f));
    public static readonly UIColor Square = Dynamic(Gray95, Ash26);
    // Read per resolve rather than captured: the dynamic providers below outlive a share, and
    // the extension's process outlives many of them. Null means "follow the system appearance".
    private static Theme? Theme => MauiPreferences.Theme;
    public static UIUserInterfaceStyle UserInterfaceStyle => Theme switch {
        UI.Theme.Light or UI.Theme.Ash => UIUserInterfaceStyle.Light,
        UI.Theme.Dark => UIUserInterfaceStyle.Dark,
        _ => UIUserInterfaceStyle.Unspecified,
    };

    public static UIColor Dynamic(UIColor light, UIColor dark)
        => Dynamic(light, light, dark);

    public static UIColor Dynamic(UIColor light, UIColor ash, UIColor dark)
        => UIColor.FromDynamicProvider(traits => Theme switch {
            UI.Theme.Light => light,
            UI.Theme.Ash => ash,
            UI.Theme.Dark => dark,
            _ => traits.UserInterfaceStyle == UIUserInterfaceStyle.Dark ? dark : light,
        });
}
