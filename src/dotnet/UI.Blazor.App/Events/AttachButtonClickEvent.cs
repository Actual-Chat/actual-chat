namespace ActualChat.UI.Blazor.App.Events;

public sealed record AttachButtonClickEvent(string AcceptTypes = "", string EditorId = "") : IUIEvent;
