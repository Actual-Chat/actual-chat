namespace ActualChat;

public class Features : IFeatures
{
    public IServiceProvider Services { get; }
    public IClientFeatures ClientFeatures { get; }
    public IServerFeatures ServerFeatures { get; }

    public Features(IServiceProvider services)
    {
        Services = services;
        ClientFeatures = services.GetRequiredService<IClientFeatures>();
        ServerFeatures = services.GetRequiredService<IServerFeatures>();
    }

    public Task<object?> Get(Type featureType, CancellationToken cancellationToken)
    {
        if (typeof(IClientFeatureDef).IsAssignableFrom(featureType))
            return ClientFeatures.Get(featureType, cancellationToken);
        return ServerFeatures.Get(featureType, cancellationToken);
    }

    Task<string> IFeatures.GetJson(TypeRef featureTypeRef, CancellationToken cancellationToken)
        => throw StandardError.NotSupported("This method isn't supported.");
}
