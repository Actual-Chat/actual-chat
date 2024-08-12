namespace ActualChat.UI.Blazor.App.Components;

public interface IMarkupView
{
    ChatEntry Entry { get; }
    Markup Markup { get; }
}

public interface IMarkupView<TMarkup> : IMarkupView
    where TMarkup : Markup
{
    new TMarkup Markup { get; set; }
}
