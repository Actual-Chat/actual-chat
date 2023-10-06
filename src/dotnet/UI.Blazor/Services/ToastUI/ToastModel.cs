namespace ActualChat.UI.Blazor.Services;

public enum ToastDismissDelay
{
    Short = 0,
    Long,
}

// Must be ref-comparable to have no issues with Blazor @key
public record ToastModel(
    string Info,
    string Icon,
    Action? Action,
    string ActionText,
    double? AutoDismissDelay)
{
    // This record relies on referential equality
    public virtual bool Equals(ToastModel? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
};
