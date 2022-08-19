namespace ActualChat.UI.Blazor.Services;

public class NavbarUI
{
    private bool _isVisible;

    public bool IsVisible {
        get => _isVisible;
        set {
            if (_isVisible == value) return;
            _isVisible = value;
            if (!value)
                IsThinPanelOpen.Value = false;
        }
    }

    public IMutableState<bool> IsThinPanelOpen { get; set; }
    public string ActiveGroupId { get; private set; } = "chats";
    public string ActiveGroupTitle { get; private set; } = "Chats";
    public Dictionary<string, Action> AddButtonAction { get; } = new (StringComparer.Ordinal);
    public event EventHandler? ActiveGroupChanged;

    public void ActivateGroup(string id, string title)
    {
        if (OrdinalEquals(id, ActiveGroupId))
            return;

        ActiveGroupId = id;
        ActiveGroupTitle = title;
        ActiveGroupChanged?.Invoke(this, EventArgs.Empty);
    }

    public NavbarUI(IStateFactory stateFactory)
        => IsThinPanelOpen = stateFactory.NewMutable(false);
}
