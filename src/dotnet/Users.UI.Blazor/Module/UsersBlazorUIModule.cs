using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;

namespace ActualChat.Users.UI.Blazor.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public partial class UsersBlazorUIModule : HostModule, IBlazorUIModule
{
    public static string ImportName => "users";

    [ServiceConstructor]
    public UsersBlazorUIModule(IServiceProvider services) : base(services) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.HasBlazorUI())
            return; // Blazor UI only module

        var fusion = services.AddFusion();

        // Matching type finder
        services.AddSingleton<IMatchingTypeRegistry>(c => new UsersBlazorUIMatchingTypeRegistry());
    }
}
