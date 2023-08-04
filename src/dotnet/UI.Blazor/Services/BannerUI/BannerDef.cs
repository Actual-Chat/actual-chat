namespace ActualChat.UI.Blazor.Services;

public sealed record BannerDef(
    RenderFragment<BannerDef> View,
    Action<BannerDef>? DismissHandler = null)
{
    public bool HasDismissHandler => DismissHandler != null;

    public void Dismiss()
        => DismissHandler?.Invoke(this);

    // This record relies on referential equality
    public bool Equals(BannerDef? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
