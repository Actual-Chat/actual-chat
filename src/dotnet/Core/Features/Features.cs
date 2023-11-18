namespace ActualChat;

#pragma warning disable CA1724 // The type name Features conflicts in whole or in part with the namespace ...

public class Features(IServiceProvider services) : IFeatures
{
    private IClientFeatures? _clientFeatures;
    private IServerFeatures? _serverFeatures;

    public IServiceProvider Services { get; } = services;
    public IClientFeatures ClientFeatures
        => _clientFeatures ??= Services.GetRequiredService<IClientFeatures>();
    public IServerFeatures ServerFeatures
        => _serverFeatures ??= Services.GetRequiredService<IServerFeatures>();

    public Task<object?> Get(Type featureType, CancellationToken cancellationToken)
        => typeof(IClientFeatureDef).IsAssignableFrom(featureType)
            ? ClientFeatures.Get(featureType, cancellationToken)
            : ServerFeatures.Get(featureType, cancellationToken);

    Task<byte[]> IFeatures.GetData(TypeRef featureTypeRef, CancellationToken cancellationToken)
        => throw StandardError.NotSupported("This method isn't supported.");
}
