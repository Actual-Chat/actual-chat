using Microsoft.Toolkit.HighPerformance.Buffers;

namespace ActualChat;

public interface IFeatures : IComputeService
{
    [ComputeMethod]
    Task<object?> Get(Type featureType, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<byte[]> GetData(TypeRef featureTypeRef, CancellationToken cancellationToken);
}

public abstract class FeaturesBase : IFeatures
{
    private ILogger? _log;

    protected IByteSerializer Serializer { get; set; } = ByteSerializer.Default;
    protected ILogger Log => _log ??= Services.LogFor(GetType());

    public IServiceProvider Services { get; }
    public IFeatureDefRegistry Registry { get; }

    protected FeaturesBase(IFeatureDefRegistry registry, IServiceProvider services)
    {
        Registry = registry;
        Services = services;
    }

    // [ComputeMethod]
    public virtual async Task<object?> Get(Type featureType, CancellationToken cancellationToken)
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

        using var buffer = new ArrayPoolBufferWriter<byte>(32);
        Serializer.Write(buffer, value, featureDef.ResultType);
        return buffer.WrittenSpan.ToArray();
    }
}
