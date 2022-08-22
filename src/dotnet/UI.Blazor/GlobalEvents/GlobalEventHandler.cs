namespace ActualChat.UI.Blazor;

public delegate Task GlobalEventHandler<in TEvent>(TEvent @event, CancellationToken cancellationToken);
