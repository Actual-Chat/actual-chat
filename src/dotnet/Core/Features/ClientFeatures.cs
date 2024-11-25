namespace ActualChat;

public interface IClientFeatures : IFeatures;

public class ClientFeatures(IServiceProvider services)
    : FeaturesBase(ClientFeatureDefRegistry.Instance, services), IClientFeatures;
