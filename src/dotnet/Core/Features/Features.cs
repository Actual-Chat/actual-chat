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
        => typeof(IClientFeatureDef).IsAssignableFrom(featureType)
            ? ClientFeatures.Get(featureType, cancellationToken)
            : ServerFeatures.Get(featureType, cancellationToken);

    Task<byte[]> IFeatures.GetData(TypeRef featureTypeRef, CancellationToken cancellationToken)
        => throw StandardError.NotSupported("This method isn't supported.");
}
