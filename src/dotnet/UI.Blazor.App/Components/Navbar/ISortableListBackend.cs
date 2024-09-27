using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App.Components;

public interface ISortableListBackend
{
    public static readonly string JSCreateMethod = $"{BlazorUIAppModule.ImportName}.SortableList.create";

    [JSInvokable]
    void OnOrderChanged(string[] ids);
}
