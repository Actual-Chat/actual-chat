using ActualChat.Flows;
using MudBlazor;

namespace ActualChat.Mui;

public sealed record FlowsFilter(string? TypeName = null, FlowListScope Scope = FlowListScope.Active)
{
    public const int RowLimit = 200;

    public bool ShowRows => !TypeName.IsNullOrEmpty() || Scope == FlowListScope.Problematic;

    public FlowsQuery ToQuery()
        => new(TypeName, ShowRows ? RowLimit : 0, Scope);

    public static Color GetStatusColor(FlowStatus status)
        => status switch {
            FlowStatus.Failed => Color.Error,
            FlowStatus.Stuck => Color.Warning,
            FlowStatus.Completed => Color.Success,
            _ => Color.Default,
        };
}
