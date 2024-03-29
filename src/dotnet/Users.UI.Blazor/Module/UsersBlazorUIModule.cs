using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Users.UI.Blazor.Components;

namespace ActualChat.Users.UI.Blazor.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class UsersBlazorUIModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IBlazorUIModule
{
    public static string ImportName => "users";

    protected override void InjectServices(IServiceCollection services)
    {
        services.AddFusion();

        // IModalViews
        services.AddTypeMap<IModalView>(map => map
            .Add<OwnAccountEditorModal.Model, OwnAccountEditorModal>()
            .Add<OwnAvatarEditorModal.Model, OwnAvatarEditorModal>()
            .Add<DeleteAccountModal.Model, DeleteAccountModal>()
        );
    }
}
