using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public class TranslationUI : UIServiceBase<AppUIHub>, IComputeService
{
    private readonly ILruCache<ChatId, Unit> _mustSuggestCache;

    public TranslationUI(AppUIHub hub) : base(hub)
        => _mustSuggestCache = new ThreadSafeLruCache<ChatId, Unit>(50, evictionHandler: InvalidateMustSuggestCache);

    private ThrottledTranslations Translations => field ??= Hub.Services.GetRequiredService<ThrottledTranslations>();
    private ChatUI ChatUI => Hub.ChatUI;
    private LanguageUI LanguageUI => Hub.LanguageUI;
    private AuthorUI AuthorUI => Hub.AuthorUI;

    [ComputeMethod]
    public virtual async Task<bool> IsSubHeaderVisible(ChatId chatId, CancellationToken cancellationToken = default) {
        var isVisible = await GetSubHeaderVisibility(chatId, cancellationToken).ConfigureAwait(false);
        if (isVisible != null)
            return isVisible.Value;

        // Keep the header visible if it was suggested to show before during the app session.
        if (await GetStoredMustSuggest(chatId, cancellationToken).ConfigureAwait(false))
            return true;

        return await MustSuggest(chatId, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual Task<bool?> IsEnabled(ChatId chatId, CancellationToken cancellationToken = default)
        => UserSettingsUI.ChatUserSettings(GetTranslationSettingsTargetChatId(chatId))
            .Get(x => x.MustTranslate, cancellationToken);

    [ComputeMethod]
    public virtual Task<bool> MustTranslateOwnMessages(
        ChatId chatId,
        CancellationToken cancellationToken = default)
        => UserSettingsUI.ChatUserSettings(GetTranslationSettingsTargetChatId(chatId))
            .Get(x => x.MustTranslateOwnMessages ?? true, cancellationToken);

    [ComputeMethod]
    public virtual async Task<Language> GetTargetLanguage(ChatId chatId, CancellationToken cancellationToken = default)
    {
        chatId = GetTranslationSettingsTargetChatId(chatId);
        var settings = await UserSettingsUI.ChatUserSettings(chatId).Get(cancellationToken).ConfigureAwait(false);
        return settings.TranslationTargetLanguage
            ?? await LanguageUI.GetChatLanguage(chatId, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<bool> MustTranslate(ChatEntry entry, bool isForStreaming, CancellationToken cancellationToken)
    {
        if (await IsEnabled(entry.ChatId, cancellationToken).ConfigureAwait(false) != true)
            return false;

        return await NeedsTranslate(entry, isForStreaming, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<bool> NeedsTranslate(ChatEntry entry, bool isForStreaming, CancellationToken cancellationToken)
    {
        if (!entry.SupportsTranslation(isForStreaming))
            return false;

        if (!isForStreaming && !entry.HasAudio)
            // always for typed text entries
            return true;

        return await IsForeignEntry(entry.Id, true, cancellationToken).ConfigureAwait(false) == true;
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
    public virtual async Task<Translation?> Get(TranslationSourceId translationSourceId, CancellationToken cancellationToken = default){
        var translationId = await ToTranslationId(translationSourceId, cancellationToken).ConfigureAwait(false);
        return await Translations
            .Get(translationId, cancellationToken)
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
       const int minForeignCount = 3;
       const double minForeignRatio = 0.3;

       var itemVisibility = await ChatUI.ItemVisibility.Use(cancellationToken).ConfigureAwait(false);
       if (itemVisibility.IsEmpty || itemVisibility.ChatId != chatId)
           return false;

       var visibleEntryIds = itemVisibility.VisibleTextEntryIds;
       var totalCount = visibleEntryIds.Count;
       if (totalCount == 0)
           return false;

       var foreignCount = 0;
       foreach (var entryId in visibleEntryIds) {
           if (await IsForeignEntry(entryId, true, cancellationToken).ConfigureAwait(false) == true)
               foreignCount++;
       }

       if (foreignCount < minForeignCount && (double)foreignCount / totalCount < minForeignRatio)
           return false;

       using (Computed.BeginIsolation())
           StoreMustSuggest(chatId);
       return true;
   }

   [ComputeMethod]
   protected virtual Task<bool> GetStoredMustSuggest(ChatId chatId, CancellationToken cancellationToken)
       => Task.FromResult(_mustSuggestCache.TryGetValue(chatId, out _));

   [ComputeMethod]
   protected virtual async Task<bool?> IsForeignEntry(ChatEntryId entryId, bool useOnlyTargetLanguage, CancellationToken cancellationToken = default)
   {
       var entry = await ChatUI.GetEntry(entryId, cancellationToken).ConfigureAwait(false);
       if (entry is null)
           return null;

       var mustTranslateOwnMessages = await MustTranslateOwnMessages(entry.ChatId, cancellationToken).ConfigureAwait(false);
       if (!mustTranslateOwnMessages) {
           var ownAuthor = await AuthorUI.GetOwn(entry.ChatId, cancellationToken).ConfigureAwait(false);
           if (ownAuthor.Id == entry.AuthorId)
               return false;
       }

       var entryLanguage = await Translations.GetLanguage(entryId, cancellationToken).ConfigureAwait(false);
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
       => UserSettingsUI.ChatUserSettings(GetTranslationSettingsTargetChatId(chatId))
           .Update(x => x with { IsTranslationSubHeaderVisible = isVisible }, cancellationToken);

   public Task SetIsOn(ChatId chatId, bool? value, CancellationToken cancellationToken = default)
       => UserSettingsUI.ChatUserSettings(GetTranslationSettingsTargetChatId(chatId))
           .Update(x => x with { MustTranslate = value }, cancellationToken);

   public Task SetMustTranslateOwnMessages(ChatId chatId, bool? value, CancellationToken cancellationToken = default)
       => UserSettingsUI.ChatUserSettings(GetTranslationSettingsTargetChatId(chatId))
           .Update(x => x with { MustTranslateOwnMessages = value }, cancellationToken);

   public Task SetTargetLanguage(ChatId chatId, Language? language, CancellationToken cancellationToken = default)
       => UserSettingsUI.ChatUserSettings(GetTranslationSettingsTargetChatId(chatId))
           .Update(x => x with { TranslationTargetLanguage = language }, cancellationToken);

   // Private methods

   private Task<bool?> GetSubHeaderVisibility(ChatId chatId, CancellationToken cancellationToken)
       => UserSettingsUI.ChatUserSettings(GetTranslationSettingsTargetChatId(chatId))
           .Get(x => x.IsTranslationSubHeaderVisible, cancellationToken);

   private async Task<TranslationId> ToTranslationId(TranslationSourceId translationSourceId, CancellationToken cancellationToken)
   {
       var targetLanguage = await GetTargetLanguage(translationSourceId.ChatId, cancellationToken).ConfigureAwait(false);
       return TranslationId.New(translationSourceId, targetLanguage);
   }

   private static ChatId GetTranslationSettingsTargetChatId(ChatId chatId)
       => chatId.IsThread(out var threadChatId) ? threadChatId.GetOutermostParent() : chatId;

   private void StoreMustSuggest(ChatId chatId)
   {
       if (!_mustSuggestCache.TryAdd(chatId, Unit.Default))
           return;

       InvalidateMustSuggestCache(chatId);
   }

   private void InvalidateMustSuggestCache(ChatId chatId, Unit _1 = default)
   {
       using (Invalidation.Begin())
           _ = GetStoredMustSuggest(chatId, default);
   }
}
