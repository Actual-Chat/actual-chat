namespace ActualChat;

public interface IClientFeatures : IFeatures;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class ClientFeatures(IServiceProvider services)
    : FeaturesBase(ClientFeatureDefRegistry.Instance, services), IClientFeatures;
