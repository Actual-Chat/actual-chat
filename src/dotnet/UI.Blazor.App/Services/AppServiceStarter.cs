using ActualChat.Audio.UI.Blazor.Services;
using ActualChat.Chat;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public class AppServiceStarter
{
    private IServiceProvider Services { get; }
    private Tracer Tracer { get; }

    public AppServiceStarter(IServiceProvider services)
    {
        Services = services;
        Tracer = Services.Tracer(GetType());
    }

    public async Task PreWebViewWarmup(CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Region(nameof(PreWebViewWarmup));
        try {
            await Task.Run(() => {
                WarmupSerializer(new Chat.Chat());
                WarmupSerializer(new ChatTile());
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Tracer.Point($"{nameof(PreWebViewWarmup)} failed, error: " + e);
        }
    }

    public Task PostSessionSetupWarmup(CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Region(nameof(PostSessionSetupWarmup));
        return Task.CompletedTask;
        /*
        try {
            await Task.Run(() => {
                var chatListUI = Services.GetRequiredService<ChatListUI>();
                _ = chatListUI.ListActive(cancellationToken);
                _ = chatListUI.ListAllUnordered(cancellationToken);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) {
            Tracer.Point($"{nameof(PostSessionSetupWarmup)} failed, error: " + e);
        }
        */
    }

    public async Task ReadyToRender(CancellationToken cancellationToken)
    {
 #pragma warning disable MA0004
        using var _1 = Tracer.Region(nameof(ReadyToRender));

        // Create history - this should be done as early as possible
        var history = Services.GetRequiredService<History>();
        _ = history.Initialize();

        // Starting AccountUI state sync
        var accountUI = Services.GetRequiredService<AccountUI>();

        // Starting BrowserInfo
        Tracer.Point("BrowserInfo.Initialize");
        var browserInfo = Services.GetRequiredService<BrowserInfo>();
        _ = browserInfo.Initialize();

        // Starting ThemeUI
        Tracer.Point("ThemeUI.Start");
        var themeUI = Services.GetRequiredService<ThemeUI>();
        themeUI.Start();

        // Starting Audio pipeline, load Workers and connect to Audio channel
        Tracer.Point("AudioInfo.Initialize");
        var audioInfo = Services.GetRequiredService<AudioInfo>();
        _ = audioInfo.Initialize();

        // Awaiting for completion of initialization tasks

        // Finishing w/ BrowserInfo
        await browserInfo.WhenReady.WaitAsync(cancellationToken);
        Tracer.Point("BrowserInfo is ready");

        var timeZoneConverter = Services.GetRequiredService<TimeZoneConverter>();
        if (timeZoneConverter is ServerSideTimeZoneConverter serverSideTimeZoneConverter)
            serverSideTimeZoneConverter.Initialize(browserInfo.UtcOffset);

        // Finishing w/ ThemeUI
        await themeUI.WhenReady.WaitAsync(cancellationToken);
        Tracer.Point("ThemeUI is ready");

        // Completing History initialization
        await history.WhenReady;
        Tracer.Point("History is ready");

        // Awaiting for account to be resolved
        await accountUI.WhenLoaded;

        var autoNavigationUI = Services.GetRequiredService<AutoNavigationUI>();
        await autoNavigationUI.AutoNavigate(cancellationToken);
#pragma warning restore MA0004
    }

    public Task AfterFirstRender(CancellationToken cancellationToken)
        => Task.Run(async () => {
            // Starting less important UI services
            Services.GetRequiredService<UIEventHub>();
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            Services.GetRequiredService<SignOutReloader>().Start();
            Services.GetRequiredService<AppPresenceReporter>().Start();
            Services.GetRequiredService<DebugUI>();
        }, cancellationToken);

    // Private methods

    private void WarmupSerializer<T>(T value)
    {
        var s = TextSerializer.Default;
        var text = "";

        // Write
        try {
            text = s.Write(value);
        }
        catch {
            // Intended
        }

        // Read
        try {
            _ = s.Read<T>(text);
        }
        catch {
            // Intended
        }
    }

    private void WarmupReplicaService<T>()
        where T: class, IComputeService
        => Services.GetRequiredService<T>();

    private void WarmupComputeService<T>()
        where T: class, IComputeService
        => Services.GetRequiredService<T>();
}
