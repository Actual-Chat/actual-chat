using ActualChat.UI.Blazor.Components.Internal;

namespace ActualChat.UI.Blazor.Components;

public partial class Modal : ComponentBase, IDisposable
{
    private FocusTrap? _focusTrap;
    private bool _mustFocus;

    [Parameter] public string Class { get; set; } = "";
    [Parameter, EditorRequired] public ModalRef Ref { get; set; } = null!;
    private ModalHost Host => Ref.Host;

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

    public void Close(bool force = false)
        => _ = Ref.Close(force);

    public ModalStepRef StepIn(string name)
        => Ref.StepIn(name);

    public bool StepBack()
        => Ref.StepBack();

    // Private methods

    private void OnClosed()
        => _mustFocus = true;

    // Event handlers

    private void OnBackgroundClick()
        => Close();
}
