namespace ActualChat;

public interface IServerFeatures : IFeatures
{ }

public class ServerFeatures : FeaturesBase, IServerFeatures
{
    public ServerFeatures(IServiceProvider services) : base(ServerFeatureDefRegistry.Instance, services) { }
}
