namespace ActualChat;

/// <summary>
/// Server-side feature computation service.
/// </summary>
public interface IServerFeatures : IFeatures;

/// <summary>
/// Implementation of <see cref="IServerFeatures"/> using server feature definitions.
/// </summary>
public class ServerFeatures(IServiceProvider services)
    : FeaturesBase(ServerFeatureDefRegistry.Instance, services), IServerFeatures;
