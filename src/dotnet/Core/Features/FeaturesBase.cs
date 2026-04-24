using CommunityToolkit.HighPerformance.Buffers;

namespace ActualChat;

/// <summary>
/// Service for computing and caching feature values.
/// </summary>
public interface IFeatures : IComputeService
{
    [ComputeMethod]
    Task<object?> Get(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type featureType,
        CancellationToken cancellationToken);
    [ComputeMethod]
    Task<byte[]> GetData(TypeRef featureTypeRef, CancellationToken cancellationToken);
}

/// <summary>
/// Base implementation of <see cref="IFeatures"/> with registry-based computation.
/// </summary>
public abstract class FeaturesBase(
    IFeatureDefRegistry registry,
    IServiceProvider services
    ) : SafeAsyncDisposableBase, IFeatures
{
    private ILogger? _log;

    protected IByteSerializer Serializer { get; set; } = ByteSerializer.Default;
    protected ILogger Log => _log ??= Services.LogFor(GetType());

    public IServiceProvider Services { get; } = services;
    public IFeatureDefRegistry Registry { get; } = registry;

    protected override Task DisposeAsync(bool disposing)
        => Task.CompletedTask;

    // [ComputeMethod]
    public virtual async Task<object?> Get(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type featureType,
        CancellationToken cancellationToken)
    {
        try {
            var featureDef = Registry.Get(featureType);
            var value = await featureDef.ComputeUntyped(Services, cancellationToken).ConfigureAwait(false);
            return value;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, "Feature computation failed for feature '{Feature}'", featureType.GetName());
            throw;
        }
    }

    // [ComputeMethod]
    public virtual async Task<byte[]> GetData(TypeRef featureTypeRef, CancellationToken cancellationToken)
    {
        var featureType = featureTypeRef.Resolve();
        var featureDef = Registry.Get(featureType);
        var value = await Get(featureType, cancellationToken).ConfigureAwait(false);

        using var buffer = new ArrayPoolBufferWriter<byte>(ArrayPools.SharedBytePool, 256);
        Serializer.Write(buffer, value, featureDef.ResultType);
        return buffer.WrittenSpan.ToArray();
    }
}
