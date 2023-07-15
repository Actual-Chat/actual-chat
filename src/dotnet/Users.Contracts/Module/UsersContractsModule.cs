using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;

namespace ActualChat.Users.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class UsersContractsModule : HostModule
{
    public UsersContractsModule(IServiceProvider moduleServices) : base(moduleServices) { }

    protected override void InjectServices(IServiceCollection services)
    {
        // Overrides default requirements for User type
        User.MustExist = Requirement.New(
            new(() => StandardError.Account.Guest()),
            (User? u) => u != null);
        User.MustBeAuthenticated = Requirement.New(
            new(() => StandardError.Account.Guest()),
            (User? u) => u?.IsAuthenticated() == true);

        // Any AccountException isn't a transient error
        var oldDefaultPreferTransient = TransientErrorDetector.DefaultPreferTransient;
        TransientErrorDetector.DefaultPreferTransient = TransientErrorDetector.New(e => {
            if (!oldDefaultPreferTransient.IsTransient(e))
                return false;

            return e switch {
                AccountException => false,
                _ => true,
            };
        });
    }
}
