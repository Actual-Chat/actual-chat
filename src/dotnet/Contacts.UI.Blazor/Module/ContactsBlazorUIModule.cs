using System.Diagnostics.CodeAnalysis;
using ActualChat.Contacts.UI.Blazor.Services;
using ActualChat.Hosting;
using ActualChat.Permissions;

namespace ActualChat.Contacts.UI.Blazor.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public partial class ContactsBlazorUIModule : HostModule, IBlazorUIModule
{
    public static string ImportName => "contacts";

    public ContactsBlazorUIModule(IServiceProvider moduleServices) : base(moduleServices) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.HasBlazorUI())
            return; // Blazor UI only module

        var fusion = services.AddFusion();
        fusion.AddService<ContactSync>(ServiceLifetime.Scoped);
        services.AddScoped<DeviceContacts>(c => new DeviceContacts());

        if (HostInfo.AppKind != AppKind.MauiApp)
            services.AddScoped<ContactsPermissionHandler>(c => new WebContactsPermissionHandler(c));
    }
}
