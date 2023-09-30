namespace ActualChat.UI.Blazor.Services.Internal;

public readonly record struct NavigationAction(
    string Description,
    Action Action
    ) : ICanBeNone<NavigationAction>
{
    public static readonly Action NoAction = () => { };
    public static NavigationAction None { get; } = default;

    private readonly string _description = Description;
    private readonly Action _action = Action;

    public bool IsNone => _action == null && _description == null;

    public string Description {
        get => _description ?? "";
        init => _description = value;
    }

    public Action Action {
        get => _action ?? NoAction;
        init => _action = value;
    }

    public override string ToString()
        => IsNone
            ? $"{GetType().Name}.None"
            : $"{GetType().Name}(\"{Description}\")";
}
