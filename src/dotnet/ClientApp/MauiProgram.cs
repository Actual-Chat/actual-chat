using ActualChat.Hosting;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Stl.Fusion.Extensions;
using ActualChat.UI.Blazor.App;

namespace ActualChat.ClientApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });
        // TODO: use resources + new EmbeddedFileProvider(typeof(MauiProgram).Assembly) ?

        var fileprovider = new EmbeddedFileProvider(typeof(MauiProgram).Assembly);
        var files = fileprovider.GetDirectoryContents("").ToArray();
        builder.Configuration.AddJsonFile(
            fileprovider,
            "appsettings.Development.json",
            // TODO: fix android fs access
            optional: true,
            reloadOnChange: false);
        builder.Configuration.AddJsonFile(
            fileprovider,
            // TODO: fix android fs access
            "appsettings.json",
            optional: true,
            reloadOnChange: false);
        var services = builder.Services;
        services.AddMauiBlazorWebView();

#if DEBUG || DEBUG_MAUI
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        services.AddLogging(logging => logging
            .AddDebug()
            .SetMinimumLevel(LogLevel.Information)
        );

        services.TryAddSingleton(builder.Configuration);
        services.AddSingleton(c => new HostInfo() {
            HostKind = HostKind.Blazor,
            RequiredServiceScopes = ImmutableHashSet<Symbol>.Empty
                .Add(ServiceScope.Client)
                .Add(ServiceScope.BlazorUI),
            Environment = "Development", // not hosting environment service, TODO: use configuration
            Configuration = c.GetRequiredService<IConfiguration>()
        });

        builder.ConfigureMauiHandlers(handlers => {
            handlers.AddHandler<IBlazorWebView, MauiBlazorWebViewHandler>();
        });

        //var settings = builder.Configuration.Get<ClientAppSettings>();
        var settings = new ClientAppSettings { BaseUri = GetBackendUrl() };
        if (string.IsNullOrWhiteSpace(settings.BaseUri))
            throw new Exception("Wrong configuration, base uri can't be empty");
        services.TryAddSingleton<ClientAppSettings>(settings);

        ConfigureServices(services, new Uri(settings.BaseUri));

        return builder.Build();
    }

    private static string GetBackendUrl()
    {
        // host address for local debugging
        // https://devblogs.microsoft.com/xamarin/debug-local-asp-net-core-web-apis-android-emulators/
        // https://developer.android.com/studio/run/emulator-networking.html
        // Unfortunately, this base address does not work WSA.
        // TODO(DF): find solution for WSA
        var ipAddress = DeviceInfo.Platform == DevicePlatform.Android ? "10.0.2.2" : "localhost";
        var backendUrl = $"http://{ipAddress}:7080";
        // To use BaseUri : https://local.actual.chat
        // We need to modify hosts file on android emulator similar how we did it for windows hosts.
        // Using instrunctions from https://csimpi.medium.com/android-emulator-add-hosts-file-f4c73447453e,
        // add line to the hosts file:
        // 10.0.2.2		local.actual.chat
        // Emulator has to be started with -writable-system flag every time to see hosts changes
        // See comments to https://stackoverflow.com/questions/41117715/how-to-edit-etc-hosts-file-in-android-studio-emulator-running-in-nougat/47622017#47622017
        return "https://local.actual.chat";
    }

    private static void ConfigureServices(IServiceCollection services, Uri baseUri)
    {
        Startup.ConfigureServices(services,
            baseUri,
            typeof(Module.BlazorUIClientAppModule) // required to enable pages discovery from this project
        ).Wait(); // wait on purpose, CreateMauiApp is synchronous.

        // Fusion services
        var fusion = services.AddFusion();
        fusion.AddBackendStatus();

        // discards Welcome screen
        services.AddSingleton(new WelcomeOptions { ByPass = true });
    }
}
