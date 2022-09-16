namespace ActualChat.UI.Blazor.Services;

public class SettingsModalUI
{
    private bool _isRightSideVisible;

    public bool IsTabsVisible {
        get => _isRightSideVisible;
        set {
            // ReSharper disable once RedundantCheckBeforeAssignment
            if (_isRightSideVisible == value)
                return;
            _isRightSideVisible = value;
        }
    }
}
