namespace ActualChat.UI.Blazor.App.Events;

public sealed record CameraSelectedEvent(string Requester, string DeviceId) : IUIEvent;
