using System.Diagnostics.CodeAnalysis;
using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Feedback;
using ActualChat.Hosting;
using ActualChat.Invite;
using ActualChat.Kvas;
using ActualChat.Media;
using ActualChat.Notification;
using ActualChat.Security;
using ActualChat.Transcription;
using ActualChat.Users;
using ActualLab.RestEase;

namespace ActualChat.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class ApiClientModule(IServiceProvider moduleServices) : HostModule(moduleServices), IAppModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        var fusion = services.AddFusion().AddAuthClient();
        var rpc = fusion.Rpc;

        // Audio
        services.AddScoped<AudioDownloader>(c => new AudioDownloader(c));
        services.AddScoped<AudioClient>(c => new AudioClient(c));
        services.AddTransient<IAudioStreamer>(c => c.GetRequiredService<AudioClient>());
        services.AddTransient<ITranscriptStreamer>(c => c.GetRequiredService<AudioClient>());

        // Chat
        fusion.AddClient<IChats>();
        fusion.AddClient<IAuthors>();
        fusion.AddClient<IRoles>();
        fusion.AddClient<IMentions>();
        fusion.AddClient<IReactions>();
        fusion.AddClient<IPlaces>();

        // Contacts
        fusion.AddClient<IContacts>();
        fusion.AddClient<IExternalContacts>();

        // Feedback
        fusion.AddClient<IFeedbacks>();

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
        fusion.AddClient<IAccounts>();
        fusion.AddClient<IAvatars>();
        fusion.AddClient<IUserPresences>();
        fusion.AddClient<IChatPositions>();
        fusion.AddClient<IPhoneAuth>();
        fusion.AddClient<IEmails>();
    }
}
