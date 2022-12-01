namespace ActualChat.UI.Blazor.Pages.VirtualListTestPage;

public record TestListItem(
    int Key,
    string Title,
    string Description = "",
    double FontSize = 1
) { }
