using ActualChat.Flows;
using MudBlazor;

namespace ActualChat.Mui;

public sealed record FlowsFilter(string? TypeName = null, bool ProblematicOnly = false, bool HideCompleted = true)
{
    public const int RowLimit = 200;

    public bool ShowRows => !TypeName.IsNullOrEmpty() || ProblematicOnly;

    public FlowsQuery ToQuery()
        => new(TypeName, ProblematicOnly, ShowRows ? RowLimit : 0, HideCompleted);

    public static Color GetStatusColor(FlowStatus status)
        => status switch {
            FlowStatus.Failed => Color.Error,
            FlowStatus.Stuck => Color.Warning,
            FlowStatus.Completed => Color.Success,
            _ => Color.Default,
        };
}
