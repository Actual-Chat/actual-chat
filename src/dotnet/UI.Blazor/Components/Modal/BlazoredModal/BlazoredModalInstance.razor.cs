using System.Diagnostics.CodeAnalysis;

namespace Blazored.Modal;

public partial class BlazoredModalInstance : IDisposable
{
    [CascadingParameter] private BlazoredModal Parent { get; set; } = default!;
    [CascadingParameter] private ModalOptions GlobalModalOptions { get; set; } = default!;

    [Parameter, EditorRequired] public RenderFragment Content { get; set; } = default!;
    [Parameter, EditorRequired] public ModalOptions Options { get; set; } = default!;
    [Parameter] public Guid Id { get; set; }

    private string? Position { get; set; }
    private string? ModalClass { get; set; }
    private string? OverlayCustomClass { get; set; }
    private bool ActivateFocusTrap { get; set; }
    public FocusTrap? FocusTrap { get; set; }

    [SuppressMessage("Style", "IDE0044:Add readonly modifier", Justification = "This is assigned in Razor code and isn't currently picked up by the tooling.")]
    private ElementReference _modalReference;
    private bool _setFocus;

    protected override void OnInitialized()
        => ConfigureInstance();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_setFocus) {
            if (FocusTrap is not null)
                await FocusTrap.SetFocus();
            _setFocus = false;
        }
    }

    /// <summary>
    /// Closes the modal.
    /// </summary>
    public Task CloseAsync()
        => Parent.DismissInstance(Id);

    private void ConfigureInstance()
    {
        Position = SetPosition();
        ModalClass = SetModalClass();
        OverlayCustomClass = SetOverlayCustomClass();
        ActivateFocusTrap = SetActivateFocusTrap();
        Parent.OnModalClosed += AttemptFocus;
    }

    private void AttemptFocus()
        => _setFocus = true;

    private string SetPosition()
    {
        if (!string.IsNullOrWhiteSpace(Options.PositionCustomClass))
            return Options.PositionCustomClass;
        if (!string.IsNullOrWhiteSpace(GlobalModalOptions.PositionCustomClass))
            return GlobalModalOptions.PositionCustomClass;
        return "position-middle";
    }

    private string SetModalClass()
    {
        var modalClass = string.Empty;
        if (!string.IsNullOrWhiteSpace(Options.Class))
            modalClass = Options.Class;
        if (string.IsNullOrWhiteSpace(modalClass) && !string.IsNullOrWhiteSpace(GlobalModalOptions.Class))
            modalClass = GlobalModalOptions.Class;
        if (string.IsNullOrWhiteSpace(modalClass)) {
            modalClass = "blazored-modal";
        }
        return modalClass;
    }

    private string SetOverlayCustomClass()
    {
        if (!string.IsNullOrWhiteSpace(Options.OverlayCustomClass))
            return Options.OverlayCustomClass;
        if (!string.IsNullOrWhiteSpace(GlobalModalOptions.OverlayCustomClass))
            return GlobalModalOptions.OverlayCustomClass;
        return string.Empty;
    }

    private bool SetActivateFocusTrap()
    {
        if (Options.ActivateFocusTrap.HasValue)
            return Options.ActivateFocusTrap.Value;
        if (GlobalModalOptions.ActivateFocusTrap.HasValue)
            return GlobalModalOptions.ActivateFocusTrap.Value;
        return true;
    }

    private Task HandleBackgroundClick()
        => CloseAsync();

    void IDisposable.Dispose()
        => Parent.OnModalClosed -= AttemptFocus;
}
