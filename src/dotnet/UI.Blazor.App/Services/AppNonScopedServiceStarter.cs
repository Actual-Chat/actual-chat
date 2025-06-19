using ActualChat.Contacts;
using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Components.Internal;
using ActualChat.UI.Blazor.Components.SideNav;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public class AppNonScopedServiceStarter(IServiceProvider services)
{
    private IServiceProvider Services { get; } = services;

    [field: AllowNull, MaybeNull]
    private HostInfo HostInfo => field ??= Services.HostInfo();
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= Services.LogFor(GetType());
    [field: AllowNull, MaybeNull]
    private Tracer Tracer => field ??= Services.TracerFor(GetType());

    public static void WarmupStaticServices(HostInfo hostInfo)
    {
        if (hostInfo.AppKind.HasJit())
            _ = Task.Run(() => {
                WarmupByteSerializer();
                WarmupNewtonsoftJsonSerializer();
                WarmupSystemJsonSerializer();
                WarmupTileRelated();
            });
        _ = Task.Run(() => {
            var markup = "**b** *i* @`a`a:chatId:1 http://google.com `code`\r\n```cs\r\ncode\r\n```";
            return new MarkupParser().Parse(markup);
        });
    }

    public Task StartNonScopedServices()
        => Task.Run(async () => {
            using var _1 = Tracer.MethodRegion();
            try {
                var startHostedServicesTask = StartHostedServices();
                if (HostInfo.HostKind.IsWasmApp()) {
                    await startHostedServicesTask.ConfigureAwait(false);
                    return; // Further code warms up some services, which isn't necessary in WASM
                }

                var session = Session.Default; // All clients use default session
                var cancellationToken = CancellationToken.None; // No cancellation here

                // Access key services
                var systemProperties = Services.GetRequiredService<ISystemProperties>();
                var accounts = Services.GetRequiredService<IAccounts>();
                _ = Services.StateFactory().NewMutable<bool>();

                var getServerApiInfoTask = systemProperties.GetServerApiInfo(cancellationToken);
                var ownAccountTask = accounts.GetOwn(session, cancellationToken);
                var preloadContactListDataTask = PreloadContacts(session, cancellationToken);

                // Complete the tasks we started earlier
                await Task.WhenAll(
                        getServerApiInfoTask,
                        ownAccountTask,
                        preloadContactListDataTask,
                        startHostedServicesTask)
                    .ConfigureAwait(false);
            }
            catch (Exception e) {
                Tracer.Point($"{nameof(StartNonScopedServices)} failed, error: " + e);
            }
        }, CancellationToken.None);

    private async Task PreloadContacts(Session session, CancellationToken cancellationToken)
    {
        using var _1 = Tracer.MethodRegion();
        // Start preloading top contacts
        // NOTE(DF): I doubt that it makes sense to run preload contacts here now,
        // because we don't know the selected place yet.
        var chats = Services.GetRequiredService<IChats>();
        var mentions = Services.GetRequiredService<IMentions>();
        var chatPositions = Services.GetRequiredService<IChatPositions>();
        var chatThreads = Services.GetRequiredService<IChatThreads>();
        var localSettings = Services.LocalSettings();
        var accountSettings = new AccountSettings(Services.ServerKvas(), session);

        var selectedChatId = await localSettings.Get<ChatId>(nameof(ChatUI.SelectedChatId), cancellationToken).ConfigureAwait(false);
        var selectedPlaceId = (PlaceId?)null;
        if (selectedChatId is not null)
            selectedPlaceId = (selectedChatId as PlaceChatId)?.PlaceId;
        Tracer.Point($"{nameof(PreloadContacts)}: {nameof(PlaceId)}: {selectedPlaceId?.ToString() ?? "null"}");

        var contacts = Services.GetRequiredService<IContacts>();
        var contactIds = await contacts.ListIds(session, selectedPlaceId, cancellationToken).ConfigureAwait(false);
        await contactIds
            .Select(PreloadContact)
            .Collect(ApiConstants.Concurrency.High, cancellationToken)
            .ConfigureAwait(false);
        return;

        async Task PreloadContact(ContactId contactId)
        {
            var chatId = contactId.ChatId;
            var contactTask = contacts.Get(session, contactId, cancellationToken);
            var chatNewsTask = chats.GetNews(session, chatId, cancellationToken);
            var lastMentionTask = mentions.GetLastOwn(session, chatId, cancellationToken);
            var chatPositionTask = chatPositions.GetOwn(session, chatId, ChatPositionKind.Read, cancellationToken);
            var userSettingsTask = accountSettings.GetUserChatSettings(chatId, cancellationToken);

            var contact = contactTask.ConfigureAwait(false);
            var news = await chatNewsTask.ConfigureAwait(false);
            var userSettings = await userSettingsTask.ConfigureAwait(false);
            var lastMention = await lastMentionTask.ConfigureAwait(false);
            var chatPosition = await chatPositionTask.ConfigureAwait(false);

            if (news?.LastTextEntry is { IsThreadStartEntry: true } lastTextEntry) {
                var threadChatId = lastTextEntry.ChatId.CreateThreadId(lastTextEntry.LocalId);
                var threadChatTask = chats.Get(session, threadChatId, cancellationToken);
                var threadCreatorTask = chatThreads.GetThreadCreator(session, threadChatId, cancellationToken);
                var threadChat = await threadChatTask.ConfigureAwait(false);
                var threadCreator = await threadCreatorTask.ConfigureAwait(false);
            }
        }
    }

    // Private methods

    private async Task StartHostedServices()
    {
        using var _ = Tracer.MethodRegion();
        var tracePrefix = nameof(StartHostedServices) + ": starting ";
        foreach (var hostedService in Services.HostedServices()) {
            Tracer.Point(tracePrefix + hostedService.GetType().Name);
            await hostedService.StartAsync(default).ConfigureAwait(true);
            await Task.Yield();
        }
    }

    private static void WarmupByteSerializer()
    {
#pragma warning disable CA1861 // Prefer 'static readonly' fields over constant array arguments
        var userId = Constants.User.Walle.UserId;
        var chatId = Constants.Chat.AnnouncementsChatId;
        var authorId = AuthorId.New(chatId, 1L);
        var account = new AccountFull(new User(userId.Value, "User"), 1);
        Warmup(PlaceId.New());
        Warmup(new Chat.Chat(chatId) { Rules = new AuthorRules(chatId, new AuthorFull(userId, authorId), account) });
        Warmup(new UserLanguageSettings() {
            Primary = Languages.English,
            Secondary = Languages.German,
        });
        Warmup(new UserOnboardingSettings());
        Warmup(new LocalOnboardingSettings());
        Warmup(new UserBubbleSettings() { ReadBubbles = ["test"] });
        Warmup(new ChatListSettings());
        Warmup(new ActiveChat[] { new(chatId) });
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
        Warmup(default(char));
        Warmup(default(bool?));
        Warmup(Symbol.Empty);
        Warmup(default(JSCallResultType));
        Warmup(default(ElementReference));
        Warmup(default(SideNavSide));
        Warmup(default(VirtualListEdge));
        Warmup(KeyValuePair.Create("", new List<string>()));
        Warmup(KeyValuePair.Create(default(Tune), new TuneInfo([])));
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
        return;

        static void Warmup<T>(T instance) {
#pragma warning disable IL2026
            var s = SystemJsonSerializer.Default;
            var json = s.Write(instance);
            s.Read<T>(json);
#pragma warning restore IL2026
        }
    }

    private static void WarmupTileRelated()
    {
        var buffer = ArrayBuffer<Tile<long>>.Lease(true);
        buffer.Release();
        // Add more methods related to Tile<long> based on ChatUI code
    }
}
