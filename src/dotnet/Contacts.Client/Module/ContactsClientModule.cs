using ActualChat.Hosting;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.Contacts.Module;

public class ContactsClientModule : HostModule
{
    public ContactsClientModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public ContactsClientModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Client))
            return; // Client-side only module

        var fusionClient = services.AddFusion().AddRestEaseClient();
        fusionClient.AddReplicaService<IContacts, IContactsClientDef>();
    }
}
