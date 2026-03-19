namespace ActualChat.UI.Blazor.Components;

public class TabRegistry<TTab> where TTab : class, ITab
{
    private readonly List<TTab> _tabs = new();

    public IReadOnlyList<TTab> Tabs => _tabs;

    public IEnumerable<TTab> SortedTabs
        => _tabs.OrderBy(x => x.Order).ThenBy(x => x.Id);

    public TTab? Find(string? id)
        => id == null ? null : _tabs.FirstOrDefault(t => t.Id == id);

    public void Register(TTab tab)
        => _tabs.Add(tab);

    public bool Unregister(TTab tab)
        => _tabs.Remove(tab);

    /// <summary>
    /// Resolves the selected tab ID after the tab set may have changed.
    /// Call this during OnAfterRender(Async).
    /// </summary>
    /// <param name="currentSortedTabs">Current sorted tabs snapshot.</param>
    /// <param name="renderedTabs">Sorted tabs snapshot from the last render.</param>
    /// <param name="renderedSelectedTab">The tab resolved as selected during the last render, or null if selection was invalid.</param>
    /// <param name="autoSelectFirst">If true and no valid selection exists, select the first sorted tab.</param>
    /// <returns>The tab ID to select, or null if no tabs exist.</returns>
    public string? ResolveSelectedTabId(
        IReadOnlyList<TTab> currentSortedTabs,
        IReadOnlyList<TTab> renderedTabs,
        TTab? renderedSelectedTab,
        bool autoSelectFirst = true)
    {
        if (renderedSelectedTab == null)
            return autoSelectFirst && currentSortedTabs.Count > 0
                ? currentSortedTabs[0].Id
                : null;

        // Find the nearest surviving tab: start from the previously selected tab
        // and walk forward through the previous render order, then fall back to the last tab.
        var newSelectedTab = renderedTabs
            .SkipWhile(x => x != renderedSelectedTab)
            .FirstOrDefault(currentSortedTabs.Contains)
            ?? currentSortedTabs.LastOrDefault();
        return newSelectedTab?.Id;
    }
}
