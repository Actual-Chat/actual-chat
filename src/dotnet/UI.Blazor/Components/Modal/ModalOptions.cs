namespace ActualChat.UI.Blazor.Components;

public sealed record ModalOptions
{
    public static ModalOptions Default { get; } = new() {
        Class = "modal",
        OverlayClass = "modal-overlay",
        UseFocusTrap = true,
    };

    public string Class { get; init; } = "";
    public string OverlayClass { get; init; } = "";
    public bool? UseFocusTrap { get; init; }
    public Action<ModalRef>? Closing { get; init; }

    public ModalOptions WithDefaults(ModalOptions defaults)
        => new() {
            Class = $"{Class} {defaults.Class}",
            OverlayClass = $"{OverlayClass} {defaults.OverlayClass}",
            UseFocusTrap = UseFocusTrap ?? defaults.UseFocusTrap,
        };
}
