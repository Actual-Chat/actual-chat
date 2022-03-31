namespace ActualChat.Chat.UI.Blazor.Components.MarkupParts;

public class MarkupPartViewBase : ComponentBase
{
    private ILogger? _log;

    // Just one dependency: it should render as quickly as possible
    [Inject] protected IServiceProvider Services { get; init; } = null!;
    protected ILogger Log => _log ??= Services.LogFor(GetType());

    [Parameter, ParameterComparer(typeof(ByReferenceParameterComparer))]
    public ChatEntry Entry { get; set; } = null!;
    [Parameter, ParameterComparer(typeof(ByReferenceParameterComparer))]
    public MarkupPart Part { get; set; } = null!;

    public override Task SetParametersAsync(ParameterView parameters)
        => this.HasChangedParameters(parameters) ? base.SetParametersAsync(parameters) : Task.CompletedTask;
}
