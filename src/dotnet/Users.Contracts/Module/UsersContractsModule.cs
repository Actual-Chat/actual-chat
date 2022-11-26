﻿using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using Stl.Plugins;

namespace ActualChat.Users.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class UsersContractsModule : HostModule
{
    public UsersContractsModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public UsersContractsModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
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
