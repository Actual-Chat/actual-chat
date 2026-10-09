using ActualLab.Fusion.Blazor;
using Microsoft.AspNetCore.Components;

namespace ActualChat.Mui;

public abstract class MuiComponent : CircuitHubComponentBase
{
    [Inject]
    protected MuiActions Actions { get; init; } = null!;
}
