using RestEase;

namespace ActualChat.Internal;

[BasePath("serverFeatures")]
public interface IServerFeaturesClientDef
{
    [Get(nameof(Get))]
    Task<object?> Get(Type featureType, CancellationToken cancellationToken);
    [Get(nameof(GetJson))]
    Task<string> GetJson(TypeRef featureTypeRef, CancellationToken cancellationToken);
}
