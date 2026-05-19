namespace ActualChat.UI.Blazor.App.Services;

public class ConversationUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private IChatMarkupHub GetChatMarkupHub(ChatId chatId) => Hub.ChatMarkupHubFactory[chatId];
    private TranslationUI TranslationUI => Hub.TranslationUI;

    [ComputeMethod]
    public virtual async Task<TranslatedConversation> GetTranslatedConversation(Conversation conversation, CancellationToken cancellationToken)
    {
        var conversationId = conversation.Id;
        var chatId = conversationId.ChatId;
        var chatMarkupHub = GetChatMarkupHub(chatId);
        var title = conversation.Title;
        var descriptionMarkup = chatMarkupHub.Parser.Parse(conversation.Description);
        var summaryMarkup = chatMarkupHub.Parser.Parse(conversation.Summary);
        var mustTranslate = await TranslationUI.MustTranslate(conversation, cancellationToken).ConfigureAwait(false);
        if (!mustTranslate)
            return new TranslatedConversation(
                conversationId,
                TranslatedText.From(title),
                TranslatedMarkup.From(descriptionMarkup),
                TranslatedMarkup.From(summaryMarkup));

        var titleTranslationTask = TranslationUI.Get(conversationId, ConversationTranslationIdKind.Title, cancellationToken);
        var descriptionTranslationTask = TranslationUI.Get(conversationId, ConversationTranslationIdKind.Description, cancellationToken);
        var summaryTranslationTask = TranslationUI.Get(conversationId, ConversationTranslationIdKind.Summary, cancellationToken);
        var titleTranslation = await titleTranslationTask.ConfigureAwait(false);
        var descriptionTranslation = await descriptionTranslationTask.ConfigureAwait(false);
        var summaryTranslation = await summaryTranslationTask.ConfigureAwait(false);
        await Task.WhenAll(titleTranslationTask, descriptionTranslationTask, summaryTranslationTask).ConfigureAwait(false);
        var translatedTitle = TranslatedText.From(conversation.Title, titleTranslation);
        var translatedDescription = TranslatedMarkup.From(conversation.Description, descriptionTranslation, chatMarkupHub, descriptionMarkup);
        var translatedSummary = TranslatedMarkup.From(conversation.Summary, summaryTranslation, chatMarkupHub, summaryMarkup);
        return new TranslatedConversation(conversationId, translatedTitle, translatedDescription, translatedSummary);
    }

    public async Task<string> GetClipboardText(TranslatedConversation conversation, CancellationToken cancellationToken = default)
    {
        var chatMarkupHub = GetChatMarkupHub(conversation.ConversationId.ChatId);
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        sb.AppendLine(conversation.Title.Text);
        sb.AppendLine();
        var description = await chatMarkupHub.ApplyMentionResolver(conversation.Description.Markup, cancellationToken).ConfigureAwait(false);
        sb.AppendLine(description.ToClipboardText());
        sb.AppendLine();
        var summary = await chatMarkupHub.ApplyMentionResolver(conversation.Summary.Markup, cancellationToken).ConfigureAwait(false);
        sb.AppendLine(summary.ToClipboardText());
        return sb.ToStringAndRelease();
    }
}

public record TranslatedConversation(
    ConversationId ConversationId,
    TranslatedText Title,
    TranslatedMarkup Description,
    TranslatedMarkup Summary);
