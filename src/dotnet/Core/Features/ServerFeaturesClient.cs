using RestEase;
using Stl.Reflection;

namespace ActualChat;

public class ServerFeaturesClient : IServerFeatures
{
    protected ITextSerializer Serializer { get; set; } = TypeDecoratingSerializer.Default;

    public IServiceProvider Services { get; }
    public IClient Client { get; }

    public ServerFeaturesClient(IServiceProvider services)
    {
        Services = services;
        Client = Services.GetRequiredService<IClient>();
    }

    public virtual async Task<object?> Get(Type featureType, CancellationToken cancellationToken)
    {
        var json = await GetJson(featureType, cancellationToken).ConfigureAwait(false);
        var result = Serializer.Read(json, typeof(object));
        return result;
    }

    public virtual Task<string> GetJson(TypeRef featureTypeRef, CancellationToken cancellationToken)
    {
        featureTypeRef = featureTypeRef.TrimAssemblyVersion();
        return Client.GetJson(featureTypeRef, cancellationToken);
    }

    // Nested types

    public interface IClient : IServerFeatures, IReplicaService
    { }

    [BasePath("serverFeatures")]
    public interface IClientDef
    {
        [Get(nameof(Get))]
        Task<object?> Get(Type featureType, CancellationToken cancellationToken);
        [Get(nameof(GetJson))]
        Task<string> GetJson(TypeRef featureTypeRef, CancellationToken cancellationToken);
    }
}
