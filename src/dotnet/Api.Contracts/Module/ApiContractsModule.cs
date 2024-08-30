using System.Diagnostics.CodeAnalysis;
using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Hosting;
using ActualChat.Invite;
using ActualChat.Kvas;
using ActualChat.Media;
using ActualChat.MLSearch;
using ActualChat.Notification;
using ActualChat.Search;
using ActualChat.Security;
using ActualChat.Streaming;
using ActualChat.Users;
using ActualLab.RestEase;

namespace ActualChat.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class ApiContractsModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IAppModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        var fusion = services.AddFusion().AddAuthClient();
        var rpc = fusion.Rpc;

        // Audio
        rpc.AddClient<IStreamServer>();
        services.AddSingleton<IStreamClient>(c => new StreamClient(c));
        services.AddSingleton<AudioDownloader>(c => new HttpClientAudioDownloader(c));

        // Chat
        fusion.AddClient<IChats>();
        fusion.AddClient<IAuthors>();
        fusion.AddClient<IRoles>();
        fusion.AddClient<IMentions>();
        fusion.AddClient<IReactions>();
        fusion.AddClient<IPlaces>();

        // Search
        fusion.AddClient<ISearch>();
        fusion.AddClient<IMLSearch>();

        // Contacts
        fusion.AddClient<IContacts>();
        fusion.AddClient<IExternalContacts>();
        fusion.AddClient<IExternalContactHashes>();

        // Invite
        fusion.AddClient<IInvites>();

        // Media
        fusion.AddClient<IMediaLinkPreviews>();

        // Notification
        fusion.AddClient<INotifications>();

        // Users
        rpc.AddClient<ISecureTokens>();
        fusion.AddClient<ISystemProperties>();
        fusion.AddClient<IMobileSessions>();
        if (HostInfo.HostKind.IsMauiApp())
            services.AddRestEase(restEase => restEase.AddClient<INativeAuthClient>());
        fusion.AddClient<IServerKvas>();
        fusion.AddClient<IServerSettings>();
        fusion.AddClient<IAccounts>();
        fusion.AddClient<IAvatars>();
        fusion.AddClient<IUserPresences>();
        fusion.AddClient<IChatPositions>();
        fusion.AddClient<IChatUsages>();
        fusion.AddClient<IPhoneAuth>();
        fusion.AddClient<IPhones>();
        fusion.AddClient<IEmails>();
        fusion.AddClient<ITimeZones>();
    }
}
