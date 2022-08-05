namespace ActualChat;

public interface IClientFeatures : IFeatures
{ }

public class ClientFeatures : Features, IClientFeatures
{
    public ClientFeatures(IServiceProvider services) : base(ClientFeatureDefRegistry.Instance, services) { }
}
