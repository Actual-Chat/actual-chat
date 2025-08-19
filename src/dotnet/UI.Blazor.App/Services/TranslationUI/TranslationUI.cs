using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public class TranslationUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    [field: AllowNull, MaybeNull]
    private ThrottledTranslations Translations => field ??= Hub.Services.GetRequiredService<ThrottledTranslations>();
    private ChatUI ChatUI => Hub.ChatUI;
    private LanguageUI LanguageUI => Hub.LanguageUI;
    private AuthorUI AuthorUI => Hub.AuthorUI;

    [ComputeMethod]
    public virtual async Task<bool> IsSubHeaderVisible(ChatId chatId, CancellationToken cancellationToken = default) {
        var isVisible = await GetSubHeaderVisibility(chatId, cancellationToken).ConfigureAwait(false);
        if (isVisible != null)
            return isVisible.Value;

        return await MustSuggest(chatId, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<bool?> IsEnabled(ChatId chatId, CancellationToken cancellationToken = default)
    {
        chatId = GetTranslationSettingsTargetChatId(chatId);
        var userChatSettings = await AccountSettings.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        return userChatSettings.MustTranslate;
    }

    [ComputeMethod]
    public virtual async Task<bool> MustTranslateOwnMessages(
        ChatId chatId,
        CancellationToken cancellationToken = default)
    {
        chatId = GetTranslationSettingsTargetChatId(chatId);
        var userChatSettings = await AccountSettings.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        return userChatSettings.MustTranslateOwnMessages ?? true;
    }

    [ComputeMethod]
    public virtual async Task<Language> GetTargetLanguage(ChatId chatId, CancellationToken cancellationToken = default)
    {
        chatId = GetTranslationSettingsTargetChatId(chatId);
        var userChatSettings = await AccountSettings.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        return userChatSettings.TranslationTargetLanguage ?? await LanguageUI.GetChatLanguage(chatId, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<bool> MustTranslate(ChatEntry entry, bool isForStreaming, CancellationToken cancellationToken)
    {
        if (await IsEnabled(entry.ChatId, cancellationToken).ConfigureAwait(false) != true)
            return false;

        return await NeedTranslate(entry, isForStreaming, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<bool> NeedTranslate(ChatEntry entry, bool isForStreaming, CancellationToken cancellationToken)
    {
        if (!entry.SupportsTranslation(isForStreaming))
            return false;

        if (!isForStreaming && !entry.HasMediaEntry)
            // always for typed text entries
            return true;

        return await IsForeignEntry(entry.Id.ToTextEntryId(), true, cancellationToken).ConfigureAwait(false) == true;
    }

    [ComputeMethod]
    public virtual async Task<bool> MustTranslate(Chat.Chat threadChat, CancellationToken cancellationToken)
    {
        var chatId = threadChat.Id;
        if (chatId is not ThreadChatId threadChatId)
            throw new ArgumentOutOfRangeException(nameof(threadChat), "Thread chat should be given.");

        var parentChatId = threadChatId.ParentChatId;
        var supportsTranslation = TranslationExt.ContentSupportsTranslation(threadChat.Title)
            || TranslationExt.ContentSupportsTranslation(threadChat.Description);
        if (!supportsTranslation)
            return false;

        if (await IsEnabled(parentChatId, cancellationToken).ConfigureAwait(false) != true)
            return false;

        // Pessimistically, consider that we require translation.
        return true;
    }

    [ComputeMethod]
    public virtual async Task<bool> MustTranslate(Conversation conversation, CancellationToken cancellationToken)
    {
        var supportsTranslation = TranslationExt.ContentSupportsTranslation(conversation.Title)
            || TranslationExt.ContentSupportsTranslation(conversation.Description)
            || TranslationExt.ContentSupportsTranslation(conversation.Summary);
        if (!supportsTranslation)
            return false;

        if (await IsEnabled(conversation.Id.ChatId, cancellationToken).ConfigureAwait(false) != true)
            return false;

        // Pessimistically, consider that we require translation.
        return true;
    }

    [ComputeMethod]
    public virtual async Task<Translation?> Get(TranslationSourceId translationSourceId, string consumerId, CancellationToken cancellationToken = default){
        var translationId = await ToTranslationId(translationSourceId, cancellationToken).ConfigureAwait(false);
        return await Translations
            .Get(translationId, consumerId, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<Translation?> GetExisting(TranslationSourceId translationSourceId, CancellationToken cancellationToken = default)
    {
        var translationId = await ToTranslationId(translationSourceId, cancellationToken).ConfigureAwait(false);
        return await Translations
            .GetExisting(translationId, cancellationToken)
            .ConfigureAwait(false);
    }

    [ComputeMethod]
   protected virtual async Task<bool> MustSuggest(ChatId chatId, CancellationToken cancellationToken)
   {
       var itemVisibility = await ChatUI.ItemVisibility.Use(cancellationToken).ConfigureAwait(false);
       if (itemVisibility.IsEmpty || itemVisibility.ChatId != chatId)
           return false;

       foreach (var entryId in itemVisibility.VisibleEntryIds)
           if (await IsForeignEntry(entryId, true, cancellationToken).ConfigureAwait(false) == true)
               return true;

       return false;
   }

   [ComputeMethod]
   protected virtual async Task<bool?> IsForeignEntry(TextEntryId entryId, bool useOnlyTargetLanguage, CancellationToken cancellationToken = default)
   {
       var session = Session;
       var entry = await ChatUI.GetEntry(entryId, cancellationToken).ConfigureAwait(false);
       if (entry is null)
           return null;

       var mustTranslateOwnMessages = await MustTranslateOwnMessages(entry.ChatId, cancellationToken).ConfigureAwait(false);
       if (!mustTranslateOwnMessages) {
           var ownAuthor = await AuthorUI.GetOwn(entry.ChatId, cancellationToken).ConfigureAwait(false);
           if (ownAuthor.Id == entry.AuthorId)
               return false;
       }

       var entryLanguage = await Translations.GetLanguage(entryId, TranslationConsumers.ChatView, cancellationToken).ConfigureAwait(false);
       if (entryLanguage == null)
           return null;

       if (entryLanguage.Languages.Length == 0)
           return false; // No languages can be detected - e.g., empty message or just numbers

       if (useOnlyTargetLanguage) {
           var targetLanguage = await GetTargetLanguage(entryId.ChatId, cancellationToken).ConfigureAwait(false);
           return entryLanguage.Languages.All(x => x != targetLanguage);
       }

       var spokenLanguages = await LanguageUI.ListSpoken(cancellationToken).ConfigureAwait(false);
       return entryLanguage.Languages.Any(x => !spokenLanguages.Contains(x));
   }

   public Task SetIsSubHeaderVisible(ChatId chatId, bool isVisible, CancellationToken cancellationToken = default)
       => AccountSettings.UpdateUserChatSettings(
           GetTranslationSettingsTargetChatId(chatId),
           x => x with { IsTranslationSubHeaderVisible = isVisible },
           cancellationToken);

   public Task SetIsOn(ChatId chatId, bool? value, CancellationToken cancellationToken = default)
        => AccountSettings.UpdateUserChatSettings(
            GetTranslationSettingsTargetChatId(chatId),
            x => x with { MustTranslate = value },
            cancellationToken);

   public Task SetMustTranslateOwnMessages(ChatId chatId, bool? value, CancellationToken cancellationToken = default)
        => AccountSettings.UpdateUserChatSettings(
            GetTranslationSettingsTargetChatId(chatId),
            x => x with { MustTranslateOwnMessages = value },
            cancellationToken);

   public Task SetTargetLanguage(ChatId chatId, Language? language, CancellationToken cancellationToken = default)
        => AccountSettings.UpdateUserChatSettings(
            GetTranslationSettingsTargetChatId(chatId),
            x => x with { TranslationTargetLanguage = language },
            cancellationToken);

   private async Task<bool?> GetSubHeaderVisibility(ChatId chatId, CancellationToken cancellationToken)
   {
       chatId = GetTranslationSettingsTargetChatId(chatId);
       var settings = await AccountSettings.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
       return settings.IsTranslationSubHeaderVisible;
   }

   private async Task<TranslationId> ToTranslationId(TranslationSourceId translationSourceId, CancellationToken cancellationToken)
   {
       var targetLanguage = await GetTargetLanguage(translationSourceId.ChatId, cancellationToken).ConfigureAwait(false);
       return TranslationId.New(translationSourceId, targetLanguage);
   }

   private ChatId GetTranslationSettingsTargetChatId(ChatId chatId)
       => chatId.IsThread(out var threadChatId) ? threadChatId.GetOutermostParent() : chatId;
}
