using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Sentry.Maui.Internal;

internal class SentryMauiOptionsSetup : ConfigureFromConfigurationOptions<SentryMauiOptions>
{
#pragma warning disable IL2026
    public SentryMauiOptionsSetup(IConfiguration config) : base(config)
    { }
#pragma warning restore IL2026

    public override void Configure(SentryMauiOptions options)
    {
        base.Configure(options);

        // NOTE: Anything set here will overwrite options set by the user.
        //       For option defaults that can be changed, use the constructor in SentryMauiOptions instead.

        // We'll initialize the SDK in SentryMauiInitializer
        options.InitializeSdk = false;

        // Global Mode makes sense for client apps
        options.IsGlobalModeEnabled = true;

        // Do not report cached crashes on startup to speedup start - we may loose crash details!
        options.InitCacheFlushTimeout = TimeSpan.Zero;

        // We'll use an event processor to set things like SDK name
        options.AddEventProcessor(new SentryMauiEventProcessor(options));

#if !PLATFORM_NEUTRAL
        // We can use MAUI's network connectivity information to inform the CachingTransport when we're offline.
        options.NetworkStatusListener = new MauiNetworkStatusListener(Connectivity.Current, options);
#endif
    }
}
