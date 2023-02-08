using ActualChat.UI.Blazor.Components.Internal;

namespace ActualChat.UI.Blazor.Components;

public partial class Modal : FusionComponentBase, IDisposable
{
    [CascadingParameter] private ModalHost Host { get; set; } = default!;
    [Parameter, EditorRequired] public ModalRef Ref { get; set; } = null!;
    [Parameter] public RenderFragment? Content { get; set; }

    private IModalRefImpl RefImpl => Ref;
    private FocusTrap? _focusTrap;
    private bool _setFocus;

    protected override void OnInitialized()
        => Host.OnModalClosed += AttemptFocus;

    void IDisposable.Dispose()
        => Host.OnModalClosed -= AttemptFocus;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_setFocus) {
            if (_focusTrap is { } focusTrap)
                await focusTrap.Focus();
            _setFocus = false;
        }
    }

    public bool Close(bool forceClose = false)
        => Ref.Close(forceClose);

    // Private methods

    private void AttemptFocus()
        => _setFocus = true;

    // Event handlers

    private void OnBackgroundClick()
        => Close();
}
