namespace ActualChat.UI.Blazor;

public delegate Task UIEventHandler<in TEvent>(TEvent @event, CancellationToken cancellationToken)
    where TEvent: class, IUIEvent;
