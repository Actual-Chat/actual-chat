namespace ActualChat;

public interface IServerFeaturesClient : IServerFeatures
{ }

public class ServerFeaturesClient : IServerFeatures
{
    protected ITextSerializer Serializer { get; set; } = TypeDecoratingSerializer.Default;

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
        var json = await GetJson(featureType, cancellationToken).ConfigureAwait(false);
        var result = Serializer.Read(json, typeof(object));
        return result;
    }

    // [ComputeMethod]
    public virtual Task<string> GetJson(TypeRef featureTypeRef, CancellationToken cancellationToken)
    {
        featureTypeRef = featureTypeRef.TrimAssemblyVersion();
        return Client.GetJson(featureTypeRef, cancellationToken);
    }
}
