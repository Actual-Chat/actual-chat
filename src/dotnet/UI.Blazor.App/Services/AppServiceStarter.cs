using ActualChat.Audio.UI.Blazor.Services;
using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public class AppServiceStarter
{
    private HostInfo? _hostInfo;

    private IServiceProvider Services { get; }
    private Tracer Tracer { get; }
    private HostInfo HostInfo => _hostInfo ??= Services.GetRequiredService<HostInfo>();

    public AppServiceStarter(IServiceProvider services)
    {
        Services = services;
        Tracer = Services.Tracer(GetType());
    }

    public Task PostSessionWarmup(Session session, CancellationToken cancellationToken)
        => Task.Run(async () => {
            // NOTE(AY): This code runs in the root scope, so you CAN'T access any scoped services here!
            using var _1 = Tracer.Region(nameof(PostSessionWarmup));
            try {
                var accounts = Services.GetRequiredService<IAccounts>();
                var contacts = Services.GetRequiredService<IContacts>();
                var chats = Services.GetRequiredService<IChats>();
                await accounts.GetOwn(session, CancellationToken.None).ConfigureAwait(false);
                // await contacts.ListIds(session, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e) {
                Tracer.Point($"{nameof(PostSessionWarmup)} failed, error: " + e);
            }
        }, cancellationToken);

    public async Task ReadyToRender(CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Region(nameof(ReadyToRender));

        var accountUI = Services.GetRequiredService<AccountUI>();

        // Creating History and BrowserInfo - this should be done as early as possible
        var history = Services.GetRequiredService<History>();
        var browserInfo = Services.GetRequiredService<BrowserInfo>();

        // Initializing them both together
        Tracer.Point("BulkInitUI.Invoke");
        var bulkInitUI = Services.GetRequiredService<BulkInitUI>();
        _ = bulkInitUI.Invoke(async bulkInit => {
            await history.Initialize(bulkInit).ConfigureAwait(false);
            await browserInfo.Initialize(bulkInit).ConfigureAwait(false);
        });

        // Starting ThemeUI
        Tracer.Point("ThemeUI.Start");
        var themeUI = Services.GetRequiredService<ThemeUI>();
        _ = Task.Run(() => themeUI.Start(), CancellationToken.None);

        // Awaiting for completion of initialization tasks.
        // NOTE(AY): it's fine to use .ConfigureAwait(false) below this point,
        //           coz tasks were started on Dispatcher thread already.

        // Finishing w/ BrowserInfo
        await browserInfo.WhenReady.ConfigureAwait(false);
        Tracer.Point("BrowserInfo is ready");

        var timeZoneConverter = Services.GetRequiredService<TimeZoneConverter>();
        if (timeZoneConverter is ServerSideTimeZoneConverter serverSideTimeZoneConverter)
            serverSideTimeZoneConverter.Initialize(browserInfo.UtcOffset);

        var autoNavigationUI = Services.GetRequiredService<AutoNavigationUI>();

        // Finishing w/ ThemeUI
        await themeUI.WhenReady.ConfigureAwait(false);
        Tracer.Point("ThemeUI is ready");

        // Awaiting for account to be resolved
        await accountUI.WhenLoaded.ConfigureAwait(true); // This .ConfigureAwait(true) is needed to run AutoNavigate
        Tracer.Point("AccountUI is ready");

        await autoNavigationUI.AutoNavigate(cancellationToken).ConfigureAwait(false);
    }

    public Task AfterFirstRender(CancellationToken cancellationToken)
        => Task.Run(async () => {
            // Starting less important UI services
            Services.GetRequiredService<UIEventHub>();

            await Task.Delay(TimeSpan.FromSeconds(0.75), cancellationToken).ConfigureAwait(false);
            Services.GetRequiredService<SignOutReloader>().Start();
            await StartHostedServices(cancellationToken).ConfigureAwait(false);
            Services.GetRequiredService<AppPresenceReporter>().Start();
            Services.GetRequiredService<DebugUI>();

            await Task.Delay(TimeSpan.FromSeconds(0.75), cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    // Private methods

    private async Task StartHostedServices(CancellationToken cancellationToken)
    {
        if (!HostInfo.AppKind.IsClient())
            return;

        using var _ = Tracer.Region(nameof(StartHostedServices));
        foreach (var hostedService in Services.HostedServices()) {
            Tracer.Point($"{nameof(StartHostedServices)}: starting {hostedService.GetType().Name}");
            await hostedService.StartAsync(default).ConfigureAwait(false);
        }
    }

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
