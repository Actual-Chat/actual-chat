namespace ActualChat;

public interface IClientFeatures : IFeatures
{ }

public class ClientFeatures : FeaturesBase, IClientFeatures
{
    public ClientFeatures(IServiceProvider services) : base(ClientFeatureDefRegistry.Instance, services) { }
}
