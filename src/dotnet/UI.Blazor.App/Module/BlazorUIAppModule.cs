using System.Diagnostics.CodeAnalysis;
using ActualChat.Audio;
using ActualChat.Hosting;
using ActualChat.MediaPlayback;
using ActualChat.Permissions;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App.Components.MarkupParts;
using ActualChat.UI.Blazor.App.Components.MarkupParts.CodeBlockMarkupView;
using ActualChat.UI.Blazor.App.Components.Settings;
using ActualChat.UI.Blazor.App.Pages.Landing;
using ActualChat.UI.Blazor.App.Pages.Test;
using ActualChat.UI.Blazor.App.Testing;
using ActualChat.UI.Blazor.Events;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.UI.Blazor.App.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class BlazorUIAppModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IBlazorUIModule
{
    public static string ImportName => "blazorApp";

    protected override void InjectServices(IServiceCollection services)
    {
        var fusion = services.AddFusion();

        // Singletons
        fusion.AddService<VirtualListTestService>();

        // Scoped / Blazor Circuit services
        services.AddScoped(c => new ChatUIHub(c));
        services.AddAlias<UIHub, ChatUIHub>(ServiceLifetime.Scoped);
        services.AddScoped(_ => new AnalyticEvents());
        services.AddScoped(c => new NavbarUI(c));
        services.AddScoped(c => new PanelsUI(c.UIHub()));
        services.AddScoped(c => new AuthorUI(c.ChatUIHub()));
        services.AddScoped(c => new EditMembersUI(c.ChatUIHub()));
        services.AddScoped<IAudioOutputController>(c => new AudioOutputController(c.UIHub()));
        services.AddScoped(c => new CachingKeyedFactory<IChatMarkupHub, ChatId, ChatMarkupHub>(c, 256).ToGeneric());

        // Chat UI
        fusion.AddService<ChatUI>(ServiceLifetime.Scoped);
        fusion.AddService<ChatListUI>(ServiceLifetime.Scoped);
        fusion.AddService<ChatAudioUI>(ServiceLifetime.Scoped);
        fusion.AddService<ChatEditorUI>(ServiceLifetime.Scoped);
        fusion.AddService<ChatPlayers>(ServiceLifetime.Scoped);
        fusion.AddService<AppActivity, ChatAppActivity>(ServiceLifetime.Scoped);
        services.AddScoped(c => new SelectionUI(c.ChatUIHub()));
        services.AddScoped(c => new ActiveChatsUI(c.ChatUIHub()));
        services.AddScoped(c => new IncomingShareUI(c.GetRequiredService<ModalUI>()));
        services.AddScoped(c => new FileUploader(c.UIHub()));
        services.AddScoped(_ => new SentAttachmentsStorage());
        services.AddScoped(_ => new PlayableTextPaletteProvider());

        // Chat activity
        services.AddScoped(c => new ChatActivity(c.ChatUIHub()));
        fusion.AddService<ChatStreamingActivity>(ServiceLifetime.Transient);

        // Settings
        services.AddSingleton(new AudioSettings());
        services.AddScoped(c => new LanguageUI(c.ChatUIHub()));

        // OnboardingUI
        services.AddScoped(c => new OnboardingUI(c.ChatUIHub()));
        services.AddAlias<IOnboardingUI, OnboardingUI>(ServiceLifetime.Scoped);

        // SearchUI
        fusion.AddService<SearchUI>(ServiceLifetime.Scoped);

        // IMarkupViews
        services.AddTypeMapper<IMarkupView>(map => map
            .Add<NewLineMarkup, NewLineMarkupView>()
            .Add<UrlMarkup, UrlMarkupView>()
            .Add<MentionMarkup, MentionView>()
            .Add<PreformattedTextMarkup, PreformattedTextMarkupView>()
            .Add<PlayableTextMarkup, PlayableTextMarkupView>()
            .Add<CodeBlockMarkup, CodeBlockMarkupView>()
            .Add<StylizedMarkup, StylizedMarkupView>()
            .Add<PlainTextMarkup, PlainTextMarkupView>()
            .Add<UnparsedTextMarkup, PlainTextMarkupView>()
            .Add<MarkupSeq, MarkupSeqView>()
            .Add<Markup, MarkupView>()
        );
        // IModalViews
        services.AddTypeMap<IModalView>(map => map
            .Add<AvatarSelectModal.Model, AvatarSelectModal>()
            .Add<VoiceSettingsModal.Model, VoiceSettingsModal>()
            .Add<ChatSettingsModal.Model, ChatSettingsModal>()
            .Add<PlaceSettingsModal.Model, PlaceSettingsModal>()
            .Add<CopyChatToPlaceModal.Model, CopyChatToPlaceModal>()
            .Add<CopyChatFromListToPlaceModal.Model, CopyChatFromListToPlaceModal>()
            .Add<AddMemberModal.Model, AddMemberModal>()
            .Add<NewChatModal.Model, NewChatModal>()
            .Add<NewPlaceModal.Model, NewPlaceModal>()
            .Add<OnboardingModal.Model, OnboardingModal>()
            .Add<PhoneVerificationModal.Model, PhoneVerificationModal>()
            .Add<EmailVerificationModal.Model, EmailVerificationModal>()
            .Add<SettingsModal.Model, SettingsModal>()
            .Add<AuthorModal.Model, AuthorModal>()
            .Add<LeaveChatConfirmationModal.Model, LeaveChatConfirmationModal>()
            .Add<ForwardMessageModal.Model, ForwardMessageModal>()
            .Add<ShareModalModel, ShareModal>()
            .Add<IncomingShareModal.Model, IncomingShareModal>()
            .Add<DownloadAppModal.Model, DownloadAppModal>()
            .Add<CopyChatToPlaceErrorModal.Model, CopyChatToPlaceErrorModal>()
        );
        // IBannerViews
        services.AddTypeMap<IBannerView>(map => map
            .Add<SwitchToWasmBanner.Model, SwitchToWasmBanner>()
        );

        services.ConfigureUIEvents(
            eventHub => eventHub.Subscribe<ShowSettingsEvent>((@event, ct) => {
                var modalUI = eventHub.Services.GetRequiredService<ModalUI>();
                _ = modalUI.Show(SettingsModal.Model.Instance, ModalOptions.FullScreen, ct);
                return Task.CompletedTask;
            }));

        services.AddScoped<AppScopedServiceStarter>(c => new AppScopedServiceStarter(c));
        services.AddSingleton<AppNonScopedServiceStarter>(c => new AppNonScopedServiceStarter(c));
        services.AddScoped<AppIconBadgeUpdater>(c => new AppIconBadgeUpdater(c.ChatUIHub()));
        services.AddScoped<AutoNavigationUI>(c => new AppAutoNavigationUI(c.UIHub()));

        if (HostInfo.HostKind.IsServerOrWasmApp())
            services.AddScoped<IDataCollectionSettingsUI>(c => new WebDataCollectionSettingsUI(c));

        fusion.AddService<AppPresenceReporter>(ServiceLifetime.Scoped);

        // IModalViews
        services.AddTypeMap<IModalView>(map => map
            .Add<LandingVideoModal.Model, LandingVideoModal>()
            .Add<PremiumFeaturesModal.Model, PremiumFeaturesModal>()
            .Add<SignInModal.Model, SignInModal>()
        );

        // Test Pages
        services.TryAddSingleton<IWebViewCrasher, NoopWebViewCrasher>();

        // Contacts
        fusion.AddService<ContactSync>(ServiceLifetime.Scoped);
        if (HostInfo.IsDevelopmentInstance && HostInfo.HostKind != HostKind.MauiApp)
            services.AddScoped<FakeDeviceContacts>().AddAlias<DeviceContacts, FakeDeviceContacts>(ServiceLifetime.Scoped);
        else
            services.AddScoped<DeviceContacts>();

        if (HostInfo.HostKind != HostKind.MauiApp)
            services.AddScoped<ContactsPermissionHandler>(c => new WebContactsPermissionHandler(c.UIHub()));

        // Users
        // IModalViews
        services.AddTypeMap<IModalView>(map => map
            .Add<OwnAccountEditorModal.Model, OwnAccountEditorModal>()
            .Add<OwnAvatarEditorModal.Model, OwnAvatarEditorModal>()
            .Add<DeleteAccountModal.Model, DeleteAccountModal>()
        );

        // Notifications
        services.AddScoped<NotificationUI>();
        services.AddAlias<INotificationUI, NotificationUI>(ServiceLifetime.Scoped);
        if (HostInfo.HostKind.IsServerOrWasmApp()) {
            services.AddTransient<IDeviceTokenRetriever>(c => new WebDeviceTokenRetriever(c));
            services.AddScoped<INotificationsPermission>(c => c.GetRequiredService<NotificationUI>());
        }

        // Streaming
        services.AddScoped<ITrackPlayerFactory>(c => new AudioTrackPlayerFactory(c));
        services.AddScoped<AudioInitializer>(c => new AudioInitializer(c.UIHub()));
        services.AddScoped<AudioRecorder>(c => new AudioRecorder(c.ChatUIHub()));
        if (HostInfo.HostKind != HostKind.MauiApp) {
            services.AddScoped<MicrophonePermissionHandler>(c => new WebMicrophonePermissionHandler(c.UIHub()));
            services.AddScoped<IRecordingPermissionRequester>(_ => new WebRecordingPermissionRequester());
        }

        // IModalViews
        services.AddTypeMap<IModalView>(map => map
            .Add<RecordingTroubleshooterModal.Model, RecordingTroubleshooterModal>()
        );
    }
}
