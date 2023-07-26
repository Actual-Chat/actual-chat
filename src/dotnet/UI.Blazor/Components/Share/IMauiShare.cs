namespace ActualChat.UI.Blazor.Components;

public interface IMauiShare
{
    public Task ShareLink(string title, string link, string text = "");
    public Task ShareText(string title, string text);
}
