namespace ActualChat;

/// <summary>
/// Client interface for accessing server features via RPC.
/// </summary>
public interface IServerFeaturesClient : IServerFeatures;

/// <summary>
/// Client-side implementation that fetches server features via <see cref="IServerFeaturesClient"/>.
/// </summary>
public class ServerFeaturesClient(IServiceProvider services) : IServerFeatures
{
    protected IByteSerializer Serializer { get; set; } = ByteSerializer.Default;

    public IServiceProvider Services { get; } = services;
    public IServerFeaturesClient Client { get; } = services.GetRequiredService<IServerFeaturesClient>();

    // [ComputeMethod]
    public virtual async Task<object?> Get(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type featureType,
        CancellationToken cancellationToken)
    {
        var featureDef = ServerFeatureDefRegistry.Instance.Get(featureType);
        var data = await GetData(featureType, cancellationToken).ConfigureAwait(false);
        var result = Serializer.Read(data, featureDef.ResultType, out _);
        return result;
    }

    // [ComputeMethod]
    public virtual Task<byte[]> GetData(TypeRef featureTypeRef, CancellationToken cancellationToken)
    {
        featureTypeRef = featureTypeRef.WithoutAssemblyVersions();
        return Client.GetData(featureTypeRef, cancellationToken);
    }
}
