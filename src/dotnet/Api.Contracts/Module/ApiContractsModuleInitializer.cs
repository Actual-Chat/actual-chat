using ActualLab.Generators;
using ActualLab.Resilience;
using ActualLab.Rpc;

namespace ActualChat.Module;

#pragma warning disable CA2255

internal static class ApiContractsModuleInitializer
{
    [ModuleInitializer]
    internal static void ModuleInitializer()
    {
        // This type initializer sets all super-early defaults

        // Session.Factory & Validator
#pragma warning disable CA2000
        Session.Factory = DefaultSessionFactory.New(new RandomStringGenerator(20, Alphabet.AlphaNumericDash.Symbols));
#pragma warning restore CA2000
        Session.Validator = session => session.Id.Length >= 20;

        // Overrides default requirements for User type
        User.MustExist = Requirement.New(
            (User? u) => u != null,
            new(() => StandardError.Account.Guest()));
        User.MustBeAuthenticated = Requirement.New(
            (User? u) => u?.IsAuthenticated() == true,
            new(() => StandardError.Account.Guest()));

        // Any AccountException isn't a transient error
        var oldPreferTransient = TransiencyResolvers.PreferTransient;
        TransiencyResolvers.PreferTransient = e => {
            var transiency = oldPreferTransient.Invoke(e);
            if (transiency is Transiency.Transient)
                return transiency;

            return e switch {
                AccountException => Transiency.NonTransient,
                _ => transiency,
            };
        };

        // Rpc - API version
        RpcDefaults.ApiVersion = RpcDefaults.BackendVersion = ApiConstants.Version;
#if false
        // Default binary serializer
        ByteSerializer.Default = MessagePackByteSerializer.Default;
        // Default caching settings
        ComputedOptions.ClientDefault = ComputedOptions.ClientDefault with {
            CacheMode = RemoteComputedCacheMode.NoCache,
        };
#endif
    }
}
