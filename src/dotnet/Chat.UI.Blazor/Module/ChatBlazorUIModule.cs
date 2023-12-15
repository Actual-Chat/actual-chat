using System.Diagnostics.CodeAnalysis;
using ActualChat.Audio;
using ActualChat.Chat.UI.Blazor.Components.MarkupParts;
using ActualChat.Chat.UI.Blazor.Components.MarkupParts.CodeBlockMarkupView;
using ActualChat.Chat.UI.Blazor.Components.Settings;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Chat.UI.Blazor.Testing;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Events;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class ChatBlazorUIModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IBlazorUIModule
{
    public static string ImportName => "chat";

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.HasBlazorUI())
            return; // Blazor UI only module

        var fusion = services.AddFusion();

        // Singletons
        fusion.AddService<VirtualListTestService>();

        // Scoped / Blazor Circuit services
        services.AddScoped(c => new ChatUIHub(c));
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
        services.AddSingleton(_ => new AudioSettings());
        services.AddScoped(c => new LanguageUI(c.ChatUIHub()));

        // OnboardingUI
        services.AddScoped(c => new OnboardingUI(c.ChatUIHub()));
        services.AddAlias<IOnboardingUI, OnboardingUI>(ServiceLifetime.Scoped);

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
            .Add<AddMemberModal.Model, AddMemberModal>()
            .Add<NewChatModal.Model, NewChatModal>()
            .Add<NewPlaceModal.Model, NewPlaceModal>()
            .Add<OnboardingModal.Model, OnboardingModal>()
            .Add<PhoneVerificationModal.Model, PhoneVerificationModal>()
            .Add<SettingsModal.Model, SettingsModal>()
            .Add<AuthorModal.Model, AuthorModal>()
            .Add<LeaveChatConfirmationModal.Model, LeaveChatConfirmationModal>()
            .Add<ForwardMessageModal.Model, ForwardMessageModal>()
            .Add<ShareModalModel, ShareModal>()
            .Add<IncomingShareModal.Model, IncomingShareModal>()
            .Add<DownloadAppModal.Model, DownloadAppModal>()
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
    }
}
