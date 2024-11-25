namespace ActualChat;

public class Features(IServiceProvider services) : IFeatures
{
    public IServiceProvider Services { get; } = services;

    [field: AllowNull, MaybeNull]
    public IClientFeatures ClientFeatures
        => field ??= Services.GetRequiredService<IClientFeatures>();

    [field: AllowNull, MaybeNull]
    public IServerFeatures ServerFeatures
        => field ??= Services.GetRequiredService<IServerFeatures>();

    public Task<object?> Get(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type featureType,
        CancellationToken cancellationToken)
        => typeof(IClientFeatureDef).IsAssignableFrom(featureType)
            ? ClientFeatures.Get(featureType, cancellationToken)
            : ServerFeatures.Get(featureType, cancellationToken);

    Task<byte[]> IFeatures.GetData(TypeRef featureTypeRef, CancellationToken cancellationToken)
        => throw StandardError.NotSupported("This method isn't supported.");
}
