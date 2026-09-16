namespace ActualChat.UI.Blazor.Components;

public enum HeaderMode
{
    Default = 0, // Standard .modal-header: title + close/back
    Custom, // Modal supplies its own fixed, full-bleed header content (hero)
    None, // No header
}
