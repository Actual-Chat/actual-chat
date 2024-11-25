namespace ActualChat;

public interface IServerFeatures : IFeatures;

public class ServerFeatures(IServiceProvider services)
    : FeaturesBase(ServerFeatureDefRegistry.Instance, services), IServerFeatures;
