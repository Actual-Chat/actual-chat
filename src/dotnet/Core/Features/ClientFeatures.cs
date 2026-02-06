namespace ActualChat;

/// <summary>
/// Client-side feature computation service.
/// </summary>
public interface IClientFeatures : IFeatures;

/// <summary>
/// Implementation of <see cref="IClientFeatures"/> using client feature definitions.
/// </summary>
public class ClientFeatures(IServiceProvider services)
    : FeaturesBase(ClientFeatureDefRegistry.Instance, services), IClientFeatures;
