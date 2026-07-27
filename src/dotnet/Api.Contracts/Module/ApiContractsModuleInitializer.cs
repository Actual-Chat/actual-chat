using ActualChat.Aot;
using ActualLab.Generators;
using ActualLab.Resilience;
using ActualLab.Rpc;
using ActualLab.Rpc.Serialization;

namespace ActualChat.Module;

#pragma warning disable CA2255

public static partial class ApiContractsModuleInitializer
{
    public static void Load() { }

    [ModuleInitializer]
    internal static void ModuleInitializer()
    {
        ApiModuleInitializer.Load();
        AotTypes.AddSource(new ApiContractsAotSource());

        // Session.Factory & Validator
#pragma warning disable CA2000
        Session.Factory = DefaultSessionFactory.New(new RandomStringGenerator(24, Alphabet.AlphaNumericDash.Symbols));
#pragma warning restore CA2000
        Session.Validator = session => SessionExt.IsValidId(session.Id);

        // Backend compute methods rarely set MinCacheDuration, and the Fusion default (zero) keeps
        // nothing alive - so a consistent computed is re-produced per read unless a dependant holds it.
        ComputedOptions.Default = ComputedOptions.Default with {
            MinCacheDuration = TimeSpan.FromSeconds(10),
        };

        // Any AccountException isn't a transient error, while a rate limit error always is
        var oldPreferTransient = TransiencyResolvers.PreferTransient;
        TransiencyResolvers.PreferTransient = e => e switch {
            AccountException => Transiency.NonTransient,
            _ => oldPreferTransient.Invoke(e),
        };

        // Rpc
        RpcByteMessageSerializer.Defaults.MaxArgumentDataSize = ApiConstants.Rpc.MaxArgumentDataSize;
        RpcTextMessageSerializer.Defaults.MaxArgumentDataSize = ApiConstants.Rpc.MaxArgumentDataSize;
        RpcDefaults.ApiVersion = RpcDefaults.BackendVersion = ApiConstants.Version;
#if false
        // Default caching settings
        ComputedOptions.ClientDefault = ComputedOptions.ClientDefault with {
            CacheMode = RemoteComputedCacheMode.NoCache,
        };
#endif
    }
}
