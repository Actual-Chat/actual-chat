namespace ActualChat.UI.Blazor.App.Components;

public abstract class MarkupViewBase<TMarkup> : FusionComponentBase, IMarkupView<TMarkup>
    where TMarkup : Markup
{
    [CascadingParameter] public ChatEntry Entry { get; set; } = null!;
    [Parameter, EditorRequired, ParameterComparer(typeof(ByRefParameterComparer))]
    public TMarkup Markup { get; set; } = null!;

    Markup IMarkupView.Markup => Markup;
}
