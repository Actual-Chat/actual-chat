namespace ActualChat.UI.Blazor.Components;

public interface IMenu : IHasId<string>
{
    bool IsShown { get; }
    string[] Arguments { get; set; }
    MenuHost Host { get; set; }

    ValueTask Show(bool mustShow = true);
    ValueTask Hide();
}
