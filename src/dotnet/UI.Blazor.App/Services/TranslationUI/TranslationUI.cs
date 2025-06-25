using ActualChat.Streaming;
using ActualChat.Transcription;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public class TranslationUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private ITranslations Translations => Hub.Translations;
    private ChatUI ChatUI => Hub.ChatUI;
    private LanguageUI LanguageUI => Hub.LanguageUI;
    private AuthorUI AuthorUI => Hub.AuthorUI;
    private IStreamClient StreamClient => Hub.StreamClient;

    [ComputeMethod]
    public virtual async Task<bool> IsSubHeaderVisible(ChatId chatId, CancellationToken cancellationToken = default) {
        var isVisible = await GetSubHeaderVisibility(chatId, cancellationToken).ConfigureAwait(false);
        if (isVisible != null)
            return isVisible.Value;

        return await MustSuggest(chatId, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<bool?> IsOn(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var userChatSettings = await AccountSettings.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        return userChatSettings.MustTranslate;
    }

    [ComputeMethod]
    public virtual async Task<bool> MustTranslateOwnMessages(
        ChatId chatId,
        CancellationToken cancellationToken = default)
    {
        var userChatSettings = await AccountSettings.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        return userChatSettings.MustTranslateOwnMessages ?? true;
    }

    [ComputeMethod]
    public virtual async Task<Language> GetTargetLanguage(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var userChatSettings = await AccountSettings.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        return userChatSettings.TranslationTargetLanguage ?? await LanguageUI.GetChatLanguage(chatId, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<bool> IsStreaming(ChatEntry entry, CancellationToken cancellationToken)
    {
        if (!await MustTranslate(entry, true, cancellationToken).ConfigureAwait(false))
            return false;

        var streamId = await GetTranscriptionStreamId(entry, cancellationToken).ConfigureAwait(false);
        return !streamId.IsEmpty;
    }

    [ComputeMethod]
    public virtual async Task<bool> MustTranslate(ChatEntry entry, bool isForStreaming, CancellationToken cancellationToken)
    {
        if (!entry.SupportsTranslation(isForStreaming))
            return false;

        if (await IsOn(entry.ChatId, cancellationToken).ConfigureAwait(false) != true)
            return false;

        if (!isForStreaming)
            return true;

        return await IsForeignEntry(entry.Id.ToTextEntryId(), true, cancellationToken).ConfigureAwait(false) == true;

    }

    [ComputeMethod]
    public virtual async Task<Translation?> Get(TextEntryId entryId, CancellationToken cancellationToken = default){
        var session = Session;
        var targetLanguage = await GetTargetLanguage(entryId.ChatId, cancellationToken).ConfigureAwait(false);
        return await Translations.Get(session, TranslationId.New(entryId, targetLanguage), cancellationToken).ConfigureAwait(false);
    }

   public async IAsyncEnumerable<TranscriptDiff> GetTranscript(ChatEntry entry, [EnumeratorCancellation] CancellationToken cancellationToken) {
        var targetLanguage = await GetTargetLanguage(entry.ChatId, cancellationToken).ConfigureAwait(false);
        var translationId = TranslationId.New(entry.Id.ToTextEntryId(), targetLanguage);
        var streamId = await GetTranscriptionStreamId(entry, cancellationToken).ConfigureAwait(false);
        if (streamId.IsEmpty)
            yield break;

        await foreach(var diff in StreamClient.GetTranslatedTranscript(streamId, translationId, cancellationToken).ConfigureAwait(false))
            yield return diff;
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

       var entryLanguage = await Translations.GetLanguage(session, entryId, cancellationToken).ConfigureAwait(false);
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

   [ComputeMethod]
   protected virtual async Task<Symbol> GetTranscriptionStreamId(ChatEntry entry, CancellationToken cancellationToken)
   {
       if (entry.IsStreaming)
           return entry.StreamId;

       // when entry finished streaming translation is still in progress
       var translation = await Get(entry.Id.ToTextEntryId(), cancellationToken).ConfigureAwait(false);
       if (translation is null || !translation.IsStreaming)
           return Symbol.Empty;

       return translation.StreamId;
   }

   public Task SetIsSubHeaderVisible(ChatId chatId, bool isVisible, CancellationToken cancellationToken = default)
       => AccountSettings.UpdateUserChatSettings(chatId, x => x with { IsTranslationSubHeaderVisible = isVisible }, cancellationToken);

   public Task SetIsOn(ChatId chatId, bool? value, CancellationToken cancellationToken = default)
        => AccountSettings.UpdateUserChatSettings(chatId, x => x with { MustTranslate = value }, cancellationToken);

   public Task SetMustTranslateOwnMessages(ChatId chatId, bool? value, CancellationToken cancellationToken = default)
        => AccountSettings.UpdateUserChatSettings(chatId, x => x with { MustTranslateOwnMessages = value }, cancellationToken);

   public Task SetTargetLanguage(ChatId chatId, Language? language, CancellationToken cancellationToken = default)
        => AccountSettings.UpdateUserChatSettings(chatId, x => x with { TranslationTargetLanguage = language }, cancellationToken);

   private async Task<bool?> GetSubHeaderVisibility(ChatId chatId, CancellationToken cancellationToken)
   {
       var settings = await AccountSettings.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
       return settings.IsTranslationSubHeaderVisible;
   }
}
