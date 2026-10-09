using ActualLab.Fusion.Blazor;
using Microsoft.AspNetCore.Components;

namespace ActualChat.Mui;

public abstract class MuiComputedComponent<T> : ComputedStateComponent<T>
{
    [Inject]
    protected MuiActions Actions { get; init; } = null!;
}
