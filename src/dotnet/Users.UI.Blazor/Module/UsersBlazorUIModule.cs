using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Users.UI.Blazor.Components;

namespace ActualChat.Users.UI.Blazor.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public partial class UsersBlazorUIModule : HostModule, IBlazorUIModule
{
    public static string ImportName => "users";

    public UsersBlazorUIModule(IServiceProvider moduleServices) : base(moduleServices) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.HasBlazorUI())
            return; // Blazor UI only module

        var fusion = services.AddFusion();

        // IModalViews
        services.AddTypeMap<IModalView>(map => map
            .Add<OwnAccountEditorModal.Model, OwnAccountEditorModal>()
            .Add<OwnAvatarEditorModal.Model, OwnAvatarEditorModal>()
            .Add<DeleteAccountModal.Model, DeleteAccountModal>()
        );
    }
}
