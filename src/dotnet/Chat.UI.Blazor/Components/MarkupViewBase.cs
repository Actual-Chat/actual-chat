namespace ActualChat.Chat.UI.Blazor.Components;

public abstract class MarkupViewBase<TMarkup> : ComponentBase, IMarkupView<TMarkup>
    where TMarkup : Markup
{
    [CascadingParameter] public ChatEntry Entry { get; set; } = null!;
    [Parameter, EditorRequired, ParameterComparer(typeof(ByReferenceParameterComparer))]
    public TMarkup Markup { get; set; } = null!;

    Markup IMarkupView.Markup => Markup;

    public override Task SetParametersAsync(ParameterView parameters)
        => this.HasChangedParameters(parameters) ? base.SetParametersAsync(parameters) : Task.CompletedTask;
}
