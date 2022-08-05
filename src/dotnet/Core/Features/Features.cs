using Stl.Reflection;

namespace ActualChat;

public interface IFeatures : IComputeService, IHasServices
{
    [ComputeMethod]
    Task<object?> Get(Type featureType, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<string> GetJson(TypeRef featureTypeRef, CancellationToken cancellationToken);
}

public abstract class Features : IFeatures
{
    protected ITextSerializer Serializer { get; set; } = TypeDecoratingSerializer.Default;

    public IServiceProvider Services { get; }
    public IFeatureDefRegistry Registry { get; }

    protected Features(IFeatureDefRegistry registry, IServiceProvider services)
    {
        Registry = registry;
        Services = services;
    }

    public virtual Task<object?> Get(Type featureType, CancellationToken cancellationToken)
    {
        var featureDef = Registry.Get(featureType);
        var value = featureDef.ComputeUntyped(Services, cancellationToken);
        return value;
    }

    public virtual async Task<string> GetJson(TypeRef featureTypeRef, CancellationToken cancellationToken)
    {
        var value = await Get(featureTypeRef.Resolve(), cancellationToken).ConfigureAwait(false);
        var json = Serializer.Write(value);
        return json;
    }
}
