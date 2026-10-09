using MudBlazor;

namespace ActualChat.Mui;

public static class MuiTheme
{
    public static readonly MudTheme Instance = new() {
        PaletteLight = new PaletteLight {
            Primary = "#3395FF",
            Secondary = "#6B7280",
            Error = "#FF3880",
            Success = "#00D12E",
            Warning = "#DC9E00",
            Background = "#F3F3F3",
            Surface = "#FFFFFF",
            TextPrimary = "#1C1C1C",
        },
        LayoutProperties = new LayoutProperties {
            DefaultBorderRadius = "8px",
        },
        Typography = new Typography {
            Default = new DefaultTypography {
                FontFamily = ["TT Commons Pro", "system-ui", "-apple-system", "Segoe UI", "sans-serif"],
            },
        },
    };
}
