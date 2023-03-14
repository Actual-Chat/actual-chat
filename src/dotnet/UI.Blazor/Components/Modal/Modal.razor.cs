using ActualChat.UI.Blazor.Components.Internal;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.Components;

public partial class Modal : FusionComponentBase, IDisposable
{
    [CascadingParameter] private ModalHost Host { get; set; } = default!;
    [Parameter, EditorRequired] public ModalRef Ref { get; set; } = null!;
    [Parameter] public RenderFragment? Content { get; set; }

    private FocusTrap? _focusTrap;
    private bool _mustFocus;

    protected override void OnInitialized()
        => Host.OnModalClosed += OnClosed;

    void IDisposable.Dispose()
        => Host.OnModalClosed -= OnClosed;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_mustFocus) {
            if (_focusTrap is { } focusTrap)
                await focusTrap.Focus();
            _mustFocus = false;
        }
    }

    public bool Close(bool force = false)
        => Ref.Close(force);

    // Private methods

    private void OnClosed()
        => _mustFocus = true;

    // Event handlers

    private void OnBackgroundClick()
        => Close();
}
