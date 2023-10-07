namespace ActualChat;

public interface IServerFeaturesClient : IServerFeatures
{ }

public class ServerFeaturesClient(IServiceProvider services) : IServerFeatures
{
    protected IByteSerializer Serializer { get; set; } = ByteSerializer.Default;

    public IServiceProvider Services { get; } = services;
    public IServerFeaturesClient Client { get; } = services.GetRequiredService<IServerFeaturesClient>();

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
