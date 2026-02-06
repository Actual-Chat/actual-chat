using ActualChat.App.Maui.IosShareExt.Services;
using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.UI.Services;
using ActualLab.Fusion.UI;
using ShareUI = ActualChat.App.Maui.IosShareExt.Services.ShareUI;

namespace ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;

public class IosHub(IServiceProvider services) : ProcessorBase, IHasServices
{
    public IServiceProvider Services { get; } = services;
    public StateFactory StateFactory { get; } = services.StateFactory();
    public UICommander UICommander { get; } = services.UICommander();

    public ICommander Commander => UICommander.Commander;
    // Some services require lazy resolution
    public Session Session => field ??= Services.GetRequiredService<Session>();
    public ISessionResolver SessionResolver => field ??= Services.GetRequiredService<ISessionResolver>();

    public IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    public IContacts Contacts => field ??= Services.GetRequiredService<IContacts>();
    public IChats Chats => field ??= Services.GetRequiredService<IChats>();
    public ShareUI ShareUI => field ??= Services.GetRequiredService<ShareUI>();
    public IconUI IconUI => field ??= Services.GetRequiredService<IconUI>();
    public IPlaces Places => field ??= Services.GetRequiredService<IPlaces>();
    public UrlMapper UrlMapper => field ??= Services.GetRequiredService<UrlMapper>();
    public IHttpClientFactory HttpClientFactory => field ??= Services.GetRequiredService<IHttpClientFactory>();
    public ShareInputs SharedData => field ??= Services.GetRequiredService<ShareInputs>();
    public ChunkedFileUploader FileUploader => field ??= Services.GetRequiredService<ChunkedFileUploader>();
}
public static class IosHubExt
{
    extension(IosHub hub)
    {
        public ILogger LogFor(Type type) => hub.Services.LogFor(type);
        public ILogger LogFor<T>() => hub.Services.LogFor<T>();
    }
}
