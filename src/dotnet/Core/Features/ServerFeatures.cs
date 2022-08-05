namespace ActualChat;

public interface IServerFeatures : IFeatures
{ }

public class ServerFeatures : Features, IServerFeatures
{
    public ServerFeatures(IServiceProvider services) : base(ServerFeatureDefRegistry.Instance, services) { }
}
