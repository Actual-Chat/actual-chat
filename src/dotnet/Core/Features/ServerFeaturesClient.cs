namespace ActualChat;

public interface IServerFeaturesClient : IServerFeatures
{ }

public class ServerFeaturesClient : IServerFeatures
{
    protected IByteSerializer Serializer { get; set; } = ByteSerializer.Default;

    public IServiceProvider Services { get; }
    public IServerFeaturesClient Client { get; }

    public ServerFeaturesClient(IServiceProvider services)
    {
        Services = services;
        Client = services.GetRequiredService<IServerFeaturesClient>();
    }

    // [ComputeMethod]
    public virtual async Task<object?> Get(Type featureType, CancellationToken cancellationToken)
    {
        var featureDef = ServerFeatureDefRegistry.Instance.Get(featureType);
        var data = await GetData(featureType, cancellationToken).ConfigureAwait(false);
        var result = Serializer.Read(data, featureDef.ResultType);
        return result;
    }

    // [ComputeMethod]
    public virtual Task<byte[]> GetData(TypeRef featureTypeRef, CancellationToken cancellationToken)
    {
        featureTypeRef = featureTypeRef.WithoutAssemblyVersions();
        return Client.GetData(featureTypeRef, cancellationToken);
    }
}
