using System.Diagnostics.CodeAnalysis;

namespace ActualChat;

public static class FeaturesExt
{
    public static async ValueTask<TResult> Get<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TFeature,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        this IFeatures features,
        CancellationToken cancellationToken)
        where TFeature : class, IFeatureDef<TResult>
    {
        var result = await features.Get(typeof(TFeature), cancellationToken).ConfigureAwait(false);
        return (TResult) result!;
    }
}
