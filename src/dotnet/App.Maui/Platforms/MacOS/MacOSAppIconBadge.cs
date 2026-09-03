using ActualChat.UI.Blazor.App.Services;
using AppKit;

namespace ActualChat.App.Maui;

public sealed class MacOSAppIconBadge : IAppIconBadge
{
    public void SetBadgeCount(int count)
        => BeginDispatchToMainThread(() => {
            NSApplication.SharedApplication.DockTile.BadgeLabel = count switch {
                <= 0 => null,
                > 99 => "99+",
                _ => count.ToString(),
            };
        });
}
