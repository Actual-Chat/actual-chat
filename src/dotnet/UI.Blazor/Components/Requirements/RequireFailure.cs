
namespace ActualChat.UI.Blazor.Components;

public class RequireFailure : RequirementComponent
{
    [Parameter, EditorRequired] public Func<Exception> ErrorFactory { get; set; } = null!;

    public override Task Require(CancellationToken cancellationToken)
        => throw ErrorFactory.Invoke();
}
