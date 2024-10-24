using ActualChat.Contacts;
using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Components.Internal;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public class AppNonScopedServiceStarter
{
    private HostInfo? _hostInfo;
    private ILogger? _log;

    private IServiceProvider Services { get; }
    private Tracer Tracer { get; }
    private HostInfo HostInfo => _hostInfo ??= Services.HostInfo();
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public AppNonScopedServiceStarter(IServiceProvider services)
    {
        Services = services;
        Tracer = Services.Tracer(GetType());
    }

    public static void WarmupStaticServices(HostInfo hostInfo)
    {
        if (hostInfo.AppKind.HasJit())
            _ = Task.Run(() => {
                WarmupByteSerializer();
                WarmupNewtonsoftJsonSerializer();
                WarmupSystemJsonSerializer();
            });
        _ = Task.Run(() => {
            var markup = "**b** *i* @`a`a:chatId:1 http://google.com `code`\r\n```cs\r\ncode\r\n```";
            return new MarkupParser().Parse(markup);
        });
    }

    public Task StartNonScopedServices()
        => Task.Run(async () => {
            using var _1 = Tracer.Region();
            try {
                var startHostedServicesTask = StartHostedServices();
                if (HostInfo.HostKind.IsWasmApp()) {
                    await startHostedServicesTask.ConfigureAwait(false);
                    return; // Further code warms up some services, which isn't necessary in WASM
                }

                var session = Session.Default; // All clients use default session
                var cancellationToken = CancellationToken.None; // No cancellation here

                // Access key services
                var accounts = Services.GetRequiredService<IAccounts>();
                Services.GetRequiredService<IChats>();
                _ = Services.StateFactory().NewMutable<bool>();

                // Preload own account
                var ownAccountTask = accounts.GetOwn(session, cancellationToken);

                await PreloadContactListData(session, cancellationToken).ConfigureAwait(false);

                // Complete the tasks we started earlier
                await ownAccountTask.ConfigureAwait(false);
                await startHostedServicesTask.ConfigureAwait(false);
            }
            catch (Exception e) {
                Tracer.Point($"{nameof(StartNonScopedServices)} failed, error: " + e);
            }
        }, CancellationToken.None);

    private async Task PreloadContactListData(Session session, CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Region();
        // Start preloading top contacts
        // NOTE(DF): I doubt that it makes sense to run preload contacts here now,
        // because we don't know selected place yet.
        var localSettings = Services.LocalSettings();
        var selectedChatIdOption = await localSettings.TryGet<ChatId>(nameof(ChatUI.SelectedChatId), cancellationToken).ConfigureAwait(false);
        var selectedPlaceId = PlaceId.None;
        if (selectedChatIdOption.IsSome(out var selectedChatId))
            selectedPlaceId = selectedChatId.PlaceId;
        Tracer.Point($"-- {nameof(PreloadContactListData)}.{nameof(PlaceId)}: '{selectedPlaceId}'");
        var contacts = Services.GetRequiredService<IContacts>();
        var contactIds = await contacts.ListIds(session, selectedPlaceId, cancellationToken).ConfigureAwait(false);
        foreach (var contactId in contactIds.Take(Constants.Contacts.MinLoadLimit))
            _ = contacts.Get(session, contactId, cancellationToken);
    }

    // Private methods

    private async Task StartHostedServices()
    {
        using var _ = Tracer.Region();
        var tasks = new List<Task>();
        var tracePrefix = nameof(StartHostedServices) + ": starting ";
        foreach (var hostedService in Services.HostedServices()) {
            Tracer.Point(tracePrefix + hostedService.GetType().Name);
            tasks.Add(hostedService.StartAsync(default));
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static void WarmupByteSerializer()
    {
#pragma warning disable CA1861 // Prefer 'static readonly' fields over constant array arguments
        var chatId = Constants.Chat.AnnouncementsChatId;
        var userId = Constants.User.Walle.UserId;
        var authorId = new AuthorId(chatId, 1L, AssumeValid.Option);
        var account = new AccountFull(new User(userId, "User"), 1);
        Warmup(new Chat.Chat(chatId) { Rules = new AuthorRules(chatId, new AuthorFull(userId, authorId), account) });
        Warmup(new UserLanguageSettings() { Primary = Languages.English, Secondary = Languages.German });
        Warmup(new UserOnboardingSettings());
        Warmup(new LocalOnboardingSettings());
        Warmup(new UserBubbleSettings() { ReadBubbles = new ApiArray<string>(new [] {"test"})});
        Warmup(new ChatListSettings());
        Warmup(new ApiArray<ActiveChat>(new[] { new ActiveChat(chatId)}));
#pragma warning restore CA1861

        static void Warmup<T>(T instance) {
#pragma warning disable IL2026
            var s = ByteSerializer.Default;
            using var buffer = s.Write(instance);
            s.Read<T>(buffer.WrittenMemory);
#pragma warning restore IL2026
        }
    }

    private static void WarmupNewtonsoftJsonSerializer()
    { }

    private static void WarmupSystemJsonSerializer()
    {
        Warmup(new VirtualListRenderState {
            RenderIndex = 1,
            Query = new VirtualListDataQuery(new Range<string>("1", "2"), new Range<double>(), new Range<int>()),
            KeyRange = new Range<string>("1", "2"),
            BeforeCount = 1,
            AfterCount = 1,
            HasVeryFirstItem = true,
            HasVeryLastItem = true,
            ScrollToKey = "1",
        });

        static void Warmup<T>(T instance) {
#pragma warning disable IL2026
            var s = SystemJsonSerializer.Default;
            var json = s.Write(instance);
            s.Read<T>(json);
#pragma warning restore IL2026
        }
    }
}
