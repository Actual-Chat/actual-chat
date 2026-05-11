namespace ActualChat.UI.Blazor.App.Events;

public sealed record JoinVideoCallCameraSelectedEvent(string DeviceId) : IUIEvent;
