namespace ActualChat;

public interface IFeatures : IComputeService, IHasServices
{
    [ComputeMethod]
    Task<object?> Get(Type featureType, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<string> GetJson(TypeRef featureTypeRef, CancellationToken cancellationToken);
}

public abstract class FeaturesBase : IFeatures
{
    private ILogger? _log;

    protected ITextSerializer Serializer { get; set; } = TypeDecoratingSerializer.Default;
    protected ILogger Log => _log ??= Services.LogFor(GetType());

    public IServiceProvider Services { get; }
    public IFeatureDefRegistry Registry { get; }

    protected FeaturesBase(IFeatureDefRegistry registry, IServiceProvider services)
    {
        Registry = registry;
        Services = services;
    }

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

    public virtual async Task<string> GetJson(TypeRef featureTypeRef, CancellationToken cancellationToken)
    {
        var value = await Get(featureTypeRef.Resolve(), cancellationToken).ConfigureAwait(false);
        var json = Serializer.Write(value);
        return json;
    }
}
