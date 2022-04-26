namespace ActualChat.Chat.UI.Blazor.Components.MarkupParts;

public abstract class MarkupViewBase<TMarkup> : ComponentBase, IMarkupView<TMarkup>
    where TMarkup : Markup
{
    private ILogger? _log;

    // Just one dependency: it should render as quickly as possible
    [Inject] protected IServiceProvider Services { get; init; } = null!;
    protected ILogger Log => _log ??= Services.LogFor(GetType());

    [Parameter, EditorRequired, ParameterComparer(typeof(ByValueParameterComparer))]
    public ChatEntry Entry { get; set; } = null!;
    [Parameter, EditorRequired, ParameterComparer(typeof(ByReferenceParameterComparer))]
    public TMarkup Markup { get; set; } = null!;

    Markup IMarkupView.Markup => Markup;

    public override Task SetParametersAsync(ParameterView parameters)
        => this.HasChangedParameters(parameters) ? base.SetParametersAsync(parameters) : Task.CompletedTask;
}
