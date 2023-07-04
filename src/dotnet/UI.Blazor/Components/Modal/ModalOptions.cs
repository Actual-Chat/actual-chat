namespace ActualChat.UI.Blazor.Components;

public sealed record ModalOptions
{
    public static ModalOptions Implicit { get; set; } = new() {
        Class = "modal",
        OverlayClass = "modal-overlay",
        UseFocusTrap = true,
    };
    public static ModalOptions Default { get; set; } = new() {
        Class = "",
        OverlayClass = "",
        UseFocusTrap = null,
    };
    public static ModalOptions FullScreen { get; set; } = Default with {
        OverlayClass = "modal-overlay-fullscreen",
    };

    public string Class { get; init; } = "";
    public string OverlayClass { get; init; } = "";
    public bool? UseFocusTrap { get; init; }
    public Action<ModalRef>? Closing { get; init; }

    public ModalOptions WithImplicit(ModalOptions? @implicit = null)
    {
        @implicit ??= Implicit;
        return new () {
            Class = $"{Class} {@implicit.Class}",
            OverlayClass = $"{OverlayClass} {@implicit.OverlayClass}",
            UseFocusTrap = UseFocusTrap ?? @implicit.UseFocusTrap,
        };
    }
}
