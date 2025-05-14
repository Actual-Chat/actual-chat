using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public abstract class MarkupViewBase<TMarkup> : FusionComponentBase<AppUIHub>, IMarkupView<TMarkup>
    where TMarkup : Markup
{
    [CascadingParameter] public ChatEntry Entry { get; set; } = null!;
    [Parameter, EditorRequired] public TMarkup Markup { get; set; } = null!;

    Markup IMarkupView.Markup => Markup;
    protected IChats Chats => Hub.Chats;
    protected IAuthors Authors => Hub.Authors;
    protected ChatUI ChatUI => Hub.ChatUI;
    protected AuthorUI AuthorUI => Hub.AuthorUI;
    protected HighlightUI HighlightUI => Hub.HighlightUI;
    protected SelectionUI SelectionUI => Hub.SelectionUI;
}
