using ActualChat.Aot;
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
        AotTypes.AddSource(new ApiContractsAotSource());
        CoreSerializerAndRpcSetup.AddGeneratedResolver(MessagePack.GeneratedMessagePackResolver.Instance);

        // Default binary serializer
        ByteSerializer.Default = MessagePackByteSerializer.Default;

        // Session.Factory & Validator
#pragma warning disable CA2000
        Session.Factory = DefaultSessionFactory.New(new RandomStringGenerator(24, Alphabet.AlphaNumericDash.Symbols));
#pragma warning restore CA2000
        Session.Validator = session => session.Id.Length >= 20;

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
        // Default caching settings
        ComputedOptions.ClientDefault = ComputedOptions.ClientDefault with {
            CacheMode = RemoteComputedCacheMode.NoCache,
        };
#endif
    }
}
