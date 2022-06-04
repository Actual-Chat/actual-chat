namespace ActualChat.UI.Blazor;

[EventHandler("onlongpress", typeof(EventArgs), enableStopPropagation: true, enablePreventDefault: true)]
[EventHandler("onswipedleft", typeof(EventArgs), enableStopPropagation: true, enablePreventDefault: true)]
[EventHandler("onswipedright", typeof(EventArgs), enableStopPropagation: true, enablePreventDefault: true)]
public static class EventHandlers
{
}
