using ActualChat.Chat.UI.Blazor.Components.MarkupParts;
using ActualChat.Chat.UI.Blazor.Components.MarkupParts.CodeBlockMarkupView;
using ActualChat.Chat.UI.Blazor.Components.NewChat;
using ActualChat.Chat.UI.Blazor.Components.Settings;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Module;

public class ChatBlazorUIMatchingTypeRegistry : IMatchingTypeRegistry
{
    public Dictionary<(Type Source, Symbol Scope), Type> GetMatchedTypes()
        => new () {
            {(typeof(AvatarSelectModal.Model), typeof(IModalView).ToSymbol()), typeof(AvatarSelectModal)},
            {(typeof(NoSecondaryLanguageModal.Model), typeof(IModalView).ToSymbol()), typeof(NoSecondaryLanguageModal)},
            {(typeof(ChatSettingsModal.Model), typeof(IModalView).ToSymbol()), typeof(ChatSettingsModal)},
            {(typeof(InviteAuthor.Model), typeof(IModalView).ToSymbol()), typeof(InviteAuthor)},
            {(typeof(NewChatModal.Model), typeof(IModalView).ToSymbol()), typeof(NewChatModal)},
            {(typeof(OnboardingModal.Model), typeof(IModalView).ToSymbol()), typeof(OnboardingModal)},
            {(typeof(SettingsModal.Model), typeof(IModalView).ToSymbol()), typeof(SettingsModal)},
            {(typeof(AuthorModal.Model), typeof(IModalView).ToSymbol()), typeof(AuthorModal)},
            {(typeof(DeleteMessageModal.Model), typeof(IModalView).ToSymbol()), typeof(DeleteMessageModal)},
            {(typeof(LeaveChatConfirmationModal.Model), typeof(IModalView).ToSymbol()), typeof(LeaveChatConfirmationModal)},

            {(typeof(SwitchToWasmBanner.Model), typeof(IBannerView<>).ToSymbol()), typeof(SwitchToWasmBanner)},

            {(typeof(CodeBlockMarkup), typeof(IMarkupView).ToSymbol()), typeof(CodeBlockMarkupView)},
            {(typeof(MarkupSeq), typeof(IMarkupView).ToSymbol()), typeof(MarkupSeqView)},
            {(typeof(MentionMarkup), typeof(IMarkupView).ToSymbol()), typeof(MentionView)},
            {(typeof(NewLineMarkup), typeof(IMarkupView).ToSymbol()), typeof(NewLineMarkupView)},
            {(typeof(PlainTextMarkup), typeof(IMarkupView).ToSymbol()), typeof(PlainTextMarkupView)},
            {(typeof(PlayableTextMarkup), typeof(IMarkupView).ToSymbol()), typeof(PlayableTextMarkupView)},
            {(typeof(PreformattedTextMarkup), typeof(IMarkupView).ToSymbol()), typeof(PreformattedTextMarkupView)},
            {(typeof(StylizedMarkup), typeof(IMarkupView).ToSymbol()), typeof(StylizedMarkupView)},
            {(typeof(Markup), typeof(IMarkupView).ToSymbol()), typeof(UnknownMarkupView)},
            {(typeof(UrlMarkup), typeof(IMarkupView).ToSymbol()), typeof(UrlMarkupView)},
        };
}
