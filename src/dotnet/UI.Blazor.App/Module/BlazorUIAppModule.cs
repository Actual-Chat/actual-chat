using ActualChat.Audio;
using ActualChat.MediaPlayback;
using ActualChat.UI.App.Services;
using ActualChat.UI.Blazor.App.Components.AudioPanel;
using ActualChat.UI.Blazor.App.Components.AudioPlayer;
using ActualChat.UI.Blazor.App.Components.VideoPanel;
using ActualChat.UI.Blazor.App.Components.MarkupParts;
using ActualChat.UI.Blazor.App.Components.MarkupParts.CodeBlockMarkupView;
using ActualChat.UI.Blazor.App.Components.Settings;
using ActualChat.UI.Blazor.App.Pages.Landing;
using ActualChat.UI.Blazor.App.Pages.Test;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App.Services.Gestures;
using ActualChat.UI.Blazor.App.Testing;
using ActualChat.UI.Blazor.Events;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.UI.Blazor.App.Module;

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
        services.AddScoped<ScopedServicesAccessor>(c => () => c); // Scoped in WASM/SSB, singleton in MAUI
        services.AddScoped(c => new AppUIHub(c));
        services.AddAlias<UIHub, AppUIHub>(ServiceLifetime.Scoped);
        services.AddScoped(_ => new AnalyticEvents());
        services.AddScoped(c => new NavbarUI(c.UIHub()));
        services.AddScoped(c => new PanelsUI(c.UIHub()));
        services.AddScoped(c => new RightPanelStoredState(c.UIHub()));
        services.AddScoped(c => new AuthorUI(c.AppUIHub()));
        services.AddScoped(c => new EditMembersUI(c.AppUIHub()));
        services.AddScoped(c => new CachingKeyedFactory<IChatMarkupHub, ChatId, ChatMarkupHub>(c, 256).ToGeneric());

        // Chat UI
        fusion.AddService<ChatUI>(ServiceLifetime.Scoped);
        fusion.AddService<ConversationUI>(ServiceLifetime.Scoped);
        fusion.AddService<ChatListUI>(ServiceLifetime.Scoped);
        fusion.AddService<NotificationsPanelUI>(ServiceLifetime.Scoped);
        fusion.AddService<ChatAudioUI>(ServiceLifetime.Scoped);
        services.AddAlias<IDebugAudioSync, ChatAudioUI>(ServiceLifetime.Transient);
        fusion.AddService<ChatVideoUI>(ServiceLifetime.Scoped);
        fusion.AddService<LocationUI>(ServiceLifetime.Scoped);
        fusion.AddService<LiveLocationReporter>(ServiceLifetime.Scoped);
        fusion.AddService<CameraUI>(ServiceLifetime.Scoped);
        services.AddScoped(c => new VideoQualityUI(c.AppUIHub()));
        fusion.AddService<AudioDiagnosticsUI>(ServiceLifetime.Scoped);
        fusion.AddService<VideoPanelLayoutCalculator>(ServiceLifetime.Transient);
        fusion.AddService<ChatEditorUI>(ServiceLifetime.Scoped);
        fusion.AddService<HighlightUI>(ServiceLifetime.Scoped);
        fusion.AddService<LocalSearchUI>(ServiceLifetime.Scoped);
        services.AddScoped<RecentMentionsUI>();
        services.AddScoped<RecentGifsUI>();
        fusion.AddService<LinkPreviewUI>(ServiceLifetime.Scoped);
        fusion.AddService<PeerBlockUI>(ServiceLifetime.Scoped);
        fusion.AddService<BackgroundActivityUI, PlaybackAndRecordingBackgroundActivityUI>(ServiceLifetime.Scoped);
        services.AddScoped(c => new SelectionUI(c.AppUIHub()));
        services.AddScoped(c => new ActiveChatsUI(c.AppUIHub()));
        services.AddScoped(c => new IncomingShareUI(c.AppUIHub()));
        services.AddScoped(_ => new SentContentStorage());
        services.AddScoped(_ => new OptimisticReactions());

        // Live stream UI
        fusion.AddService<LiveStreamUI>(ServiceLifetime.Scoped);
        fusion.AddService<IncomingVoiceActivityUI>(ServiceLifetime.Scoped);
        services.AddScoped(c => new WalkieTalkieReplyUI(c.AppUIHub()));
        if (!HostInfo.HostKind.IsMauiApp())
            services.AddScoped(_ => new SensorFeed()); // MauiSensorFeed is registered in MauiAppModule
        services.AddScoped(c => new GestureUI(c.AppUIHub()));
        services.AddScoped(c => new WalkieTalkieSessionCore(c.AppUIHub()));
        fusion.AddService<LiveSessionUI>(ServiceLifetime.Scoped);
        fusion.AddService<LiveBlockUI>(ServiceLifetime.Scoped);
        fusion.AddService<IncomingCallUI>(ServiceLifetime.Scoped);
        fusion.AddService<ChatActivityUI>(ServiceLifetime.Scoped);

        // Settings
        services.AddSingleton(_ => new AudioSettings()); // Used in StreamingServiceModule as well
        fusion.AddService<LanguageUI>(ServiceLifetime.Scoped);

        // OnboardingUI
        services.AddScoped(c => new OnboardingUI(c.AppUIHub()));
        services.AddAlias<IOnboardingUI, OnboardingUI>(ServiceLifetime.Scoped);

        // SearchUI
        fusion.AddService<SearchUI>(ServiceLifetime.Scoped);

        // TranslationUI
        fusion.AddService<TranslationUI>(ServiceLifetime.Scoped);
        fusion.AddService<ThrottledTranslations>(ServiceLifetime.Scoped);

        // TranscriptUI
        fusion.AddService<TranscriptUI>(ServiceLifetime.Scoped);

        // IMarkupViews
        services.AddTypeMapper<IMarkupView>(map => map
            .Add<NewLineMarkup, NewLineMarkupView>()
            .Add<UrlMarkup, UrlMarkupView>()
            .Add<HashtagMarkup, HashtagMarkupView>()
            .Add<MentionMarkup, AuthorMentionView>()
            .Add<UserMention, UserMentionView>()
            .Add<ChatMention, ChatMentionView>()
            .Add<PlaceMention, PlaceMentionView>()
            .Add<EmojiMention, EmojiMentionView>()
            .Add<PreformattedTextMarkup, PreformattedTextMarkupView>()
            .Add<PlayableTextMarkup, PlayableTextMarkupView>()
            .Add<CodeBlockMarkup, CodeBlockMarkupView>()
            .Add<StylizedMarkup, StylizedMarkupView>()
            .Add<PlainTextMarkup, PlainTextMarkupView>()
            .Add<UnparsedTextMarkup, PlainTextMarkupView>()
            .Add<MarkupSeq, MarkupSeqView>()
            .Add<Markup, MarkupView>()
            .Add<ListMarkup, ListMarkupView>()
            .Add<ParagraphMarkup, ParagraphMarkupView>()
            .Add<HeaderMarkup, HeaderMarkupView>()
            .Add<BlockQuoteMarkup, BlockQuoteMarkupView>()
        );
        // IModalViews
        services.AddTypeMap<IModalView>(map => map
            .Add<AvatarSelectModal.Model, AvatarSelectModal>()
            .Add<ChatQuickNavModal.Model, ChatQuickNavModal>()
            .Add<KeyboardShortcutsModal.Model, KeyboardShortcutsModal>()
            .Add<VoiceSettingsModal.Model, VoiceSettingsModal>()
            .Add<ChatSettingsModal.Model, ChatSettingsModal>()
            .Add<PlaceSettingsModal.Model, PlaceSettingsModal>()
            .Add<CopyChatToPlaceModal.Model, CopyChatToPlaceModal>()
            .Add<CopyChatFromListToPlaceModal.Model, CopyChatFromListToPlaceModal>()
            .Add<AddMemberModal.Model, AddMemberModal>()
            .Add<NewChatModal.Model, NewChatModal>()
            .Add<NewPlaceModal.Model, NewPlaceModal>()
            .Add<NewThreadModal.Model, NewThreadModal>()
            .Add<OnboardingModal.Model, OnboardingModal>()
            .Add<PhoneVerificationModal.Model, PhoneVerificationModal>()
            .Add<EmailVerificationModal.Model, EmailVerificationModal>()
            .Add<SettingsModal.Model, SettingsModal>()
            .Add<AuthorModal.Model, AuthorModal>()
            .Add<PeerContactEditorModal.Model, PeerContactEditorModal>()
            .Add<LeaveChatConfirmationModal.Model, LeaveChatConfirmationModal>()
            .Add<ForwardMessageModal.Model, ForwardMessageModal>()
            .Add<PttChatPickerModal.Model, PttChatPickerModal>()
            .Add<ShareModalModel, ShareModal>()
            .Add<IncomingShareModal.Model, IncomingShareModal>()
            .Add<DownloadAppModal.Model, DownloadAppModal>()
            .Add<CopyChatToPlaceErrorModal.Model, CopyChatToPlaceErrorModal>()
            .Add<TranslationTargetLanguageModal.Model, TranslationTargetLanguageModal>()
            .Add<JoinVideoCallModal.Model, JoinVideoCallModal>()
            .Add<ScreenCastAlreadyActiveModal.Model, ScreenCastAlreadyActiveModal>()
            .Add<VideoDiagnosticsModal.Model, VideoDiagnosticsModal>()
            .Add<AudioDiagnosticsModal.Model, AudioDiagnosticsModal>()
            .Add<IncomingCallModal.Model, IncomingCallModal>()
            .Add<TimeZoneEditorModal.Model, TimeZoneEditorModal>()
            .Add<ApiKeyCreateModal.Model, ApiKeyCreateModal>()
            .Add<EmojiModal.Model, EmojiModal>()
            .Add<ReportModal.Model, ReportModal>()
            .Add<LocationMapModal.Model, LocationMapModal>()
            .Add<ShareLocationModal.Model, ShareLocationModal>()
        );
        // IBannerViews
        services.AddTypeMap<IBannerView>(map => map
            .Add<SwitchToWasmBanner.Model, SwitchToWasmBanner>()
        );

        services.ConfigureUIEvents(
            eventHub => eventHub.Subscribe<ShowSettingsEvent>((@event, ct) => {
                var modalUI = eventHub.Services.GetRequiredService<ModalUI>();
                _ = modalUI.Show(new SettingsModal.Model(@event.TabId), ModalOptions.FullScreen, ct);
                return Task.CompletedTask;
            }));

        services.AddScoped<AppScopedServiceStarter>(c => new AppScopedServiceStarter(c.AppUIHub()));
        services.AddSingleton<AppNonScopedServiceStarter>(c => new AppNonScopedServiceStarter(c));
        services.AddScoped<AppIconBadgeUpdater>(c => new AppIconBadgeUpdater(c.AppUIHub()));
        services.AddScoped<NotificationReconciler>(c => new NotificationReconciler(c.AppUIHub()));

        if (HostInfo.HostKind.IsServerOrWasmApp()) {
            services.AddScoped<IDataCollectionSettingsUI>(c => new WebDataCollectionSettingsUI(c));
            services.AddScoped<IDeviceNotifications>(c => new WebDeviceNotifications(c.AppUIHub()));
        }

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
            .Add<PicCropModal.Model, PicCropModal>()
            .Add<DeleteAccountModal.Model, DeleteAccountModal>()
        );

        // Notifications
        services.AddScoped<NotificationUI>();
        services.AddAlias<INotificationUI, NotificationUI>(ServiceLifetime.Scoped);
        if (HostInfo.HostKind.IsServerOrWasmApp()) {
            services.AddTransient<IDeviceTokenRetriever>(c => new WebDeviceTokenRetriever(c));
            services.AddScoped<INotificationsPermission>(c => c.GetRequiredService<NotificationUI>());
        }

        // Audio
        services.AddScoped<ITrackPlayerFactory>(c => new AudioTrackPlayerFactory(c));
        services.AddScoped<PlaybackLagTracker>(c => new PlaybackLagTracker(c.GetRequiredService<MomentClockSet>()));
        services.AddScoped<AudioRecorder>(c => new AudioRecorder(c.AppUIHub()));
        services.AddScoped<IAudioRecorderBackend>(c => c.GetRequiredService<AudioRecorder>());
        services.AddScoped<RecordingActivityClient>(c => new RecordingActivityClient(c.UIHub()));
        if (HostInfo.HostKind != HostKind.MauiApp) {
            services.AddScoped<IAudioInitializer>(c => new AudioInitializer(c.UIHub()));
            services.AddScoped<IAudioRecorderEngine>(c => new WebRecorderEngine(c.AppUIHub()));
            services.AddScoped<IAudioPlaybackEngineFactory>(c => new WebAudioPlaybackEngineFactory(c));
            services.AddScoped<MicrophonePermissionHandler>(c => new WebMicrophonePermissionHandler(c.UIHub()));
            services.AddScoped<CameraPermissionHandler>(c => new WebCameraPermissionHandler(c.UIHub()));
            services.AddScoped<ILocationTracker>(c => new WebLocationTracker(c.AppUIHub()));
            services.AddScoped<LocationPermissionHandler>(c => new WebLocationPermissionHandler(c.UIHub()));
            services.AddScoped<IRecordingPermissionRequester>(_ => new WebRecordingPermissionRequester());
            services.AddScoped<IMediaMetadataUI>(_ => new WebMediaMetadataUI());
        }
        services.AddScoped(c => new AudioWidget(c.AppUIHub()));
        services.AddScoped(c => new AudioAttachmentPlayer(c.AppUIHub()));

        // IModalViews
        services.AddTypeMap<IModalView>(map => map
            .Add<RecordingTroubleshooterModal.Model, RecordingTroubleshooterModal>()
            .Add<PhotoTroubleshooterModal.Model, PhotoTroubleshooterModal>()
            .Add<LocationTroubleshooterModal.Model, LocationTroubleshooterModal>()
        );

        // DebugUI - override base registration to wire guide handlers
        services.AddScoped(c => {
            var hub = c.UIHub();
            var debugUI = new DebugUI(hub);
            debugUI.ShowMicTroubleshooterHandler = guideType => hub.ModalUI.Show(
                new RecordingTroubleshooterModal.Model(ParseGuideType(guideType)));
            debugUI.ShowPhotoTroubleshooterHandler = () => hub.ModalUI.Show(new PhotoTroubleshooterModal.Model());
            debugUI.ShowLocationTroubleshooterHandler = guideType => hub.ModalUI.Show(
                new LocationTroubleshooterModal.Model(ParseGuideType(guideType)));
            debugUI.ShowIncomingShareModalHandler = () => hub.ModalUI.Show(
                new IncomingShareModal.Model("Test shared text"));
            debugUI.TestVideoRecordingQualityChangeHandler = period =>
                c.GetRequiredService<VideoQualityUI>().BeginRecordingQualityTest(period);
            debugUI.TestVideoPlaybackQualityChangeHandler = period =>
                c.GetRequiredService<VideoQualityUI>().BeginPlaybackQualityTest(period);
            return debugUI;
        });

        // Sending messages & File uploads
        services.AddScoped(c => new FilePreviews(c.JSRuntime(), c.LogFor<FilePreviews>()));
        fusion.AddService<ChatSendingMessagesTriggers>(ServiceLifetime.Scoped);
        services.AddScoped(c => new SendingMessages(c.AppUIHub()));
        services.AddScoped<VideoTranscoder>();
        services.AddScoped<IUploadSessionRepo>(c => new UploadSessionRepo(c));
        services.AddScoped<UploadSessions>(c => new UploadSessions(c.AppUIHub()));
        services.AddScoped(c => new AttachmentsController(c.AppUIHub()));
        fusion.AddService<AttachmentsState>(ServiceLifetime.Scoped);
        fusion.AddService<UploadSessionsState>(ServiceLifetime.Scoped);
        services.AddScoped(c => new IncomingShareAfterSendMessageHandler(c.AppUIHub()));
    }

    // Private methods

    private static GuideType? ParseGuideType(string? guideType)
        // A null result means "auto-detect from HostInfo + BrowserInfo".
        => Enum.TryParse<GuideType>(guideType, true, out var result) ? result : null;
}
