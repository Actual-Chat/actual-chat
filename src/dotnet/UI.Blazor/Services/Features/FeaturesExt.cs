namespace ActualChat.UI.Blazor.Services;

public static class FeaturesExt
{
    public static ValueTask<bool> IsIncompleteUIEnabled(
        this IFeatures features,
        CancellationToken cancellationToken = default)
        => features.Get<Features_EnableIncompleteUI>(cancellationToken);
}
