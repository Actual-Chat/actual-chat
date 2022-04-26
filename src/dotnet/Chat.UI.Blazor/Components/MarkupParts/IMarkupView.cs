namespace ActualChat.Chat.UI.Blazor.Components.MarkupParts;

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
