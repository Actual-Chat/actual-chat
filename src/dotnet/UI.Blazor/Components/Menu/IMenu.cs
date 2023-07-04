namespace ActualChat.UI.Blazor.Components;

public interface IMenu : IHasId<string>
{
    string[] Arguments { get; set; }
    MenuHost Host { get; set; }
    Task WhenClosed { get; }
}
