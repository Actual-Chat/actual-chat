using System.Diagnostics.CodeAnalysis;
using ActualChat.Contacts.UI.Blazor.Services;
using ActualChat.Hosting;
using ActualChat.Permissions;

namespace ActualChat.Contacts.UI.Blazor.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class ContactsBlazorUIModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IBlazorUIModule
{
    public static string ImportName => "contacts";

    protected override void InjectServices(IServiceCollection services)
    {
        var fusion = services.AddFusion();
        fusion.AddService<ContactSync>(ServiceLifetime.Scoped);
        if (HostInfo.IsDevelopmentInstance && HostInfo.HostKind != HostKind.MauiApp)
            services.AddScoped<FakeDeviceContacts>().AddAlias<DeviceContacts, FakeDeviceContacts>(ServiceLifetime.Scoped);
        else
            services.AddScoped<DeviceContacts>();

        if (HostInfo.HostKind != HostKind.MauiApp)
            services.AddScoped<ContactsPermissionHandler>(c => new WebContactsPermissionHandler(c.UIHub()));
    }
}
