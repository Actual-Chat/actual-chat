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

        var streamId = await GetStreamId(entry, cancellationToken).ConfigureAwait(false);
        return !streamId.IsEmpty;
    }

    [ComputeMethod]
    public virtual async Task<bool> IsStreaming(ChatEntry entry, bool? isRealtime, CancellationToken cancellationToken)
    {
        if (!await MustTranslate(entry, true, cancellationToken).ConfigureAwait(false))
            return false;

        var streamId = await GetStreamId(entry, cancellationToken).ConfigureAwait(false);
        if (streamId.IsEmpty)
            return false;

        var translation = await Get(entry.Id.ToTextEntryId(), cancellationToken).ConfigureAwait(false);
        return isRealtime is null || (translation?.IsRealtime ?? true) == isRealtime;
    }

    [ComputeMethod]
    public virtual async Task<bool> MustTranslate(ChatEntry entry, bool isForStreaming, CancellationToken cancellationToken)
    {
        if (await IsOn(entry.ChatId, cancellationToken).ConfigureAwait(false) != true)
            return false;

        return await NeedTranslate(entry, isForStreaming, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<bool> NeedTranslate(ChatEntry entry, bool isForStreaming, CancellationToken cancellationToken)
    {
        if (!entry.SupportsTranslation(isForStreaming))
            return false;

        if (!isForStreaming)
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

        if (await IsOn(parentChatId, cancellationToken).ConfigureAwait(false) != true)
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

        if (await IsOn(conversation.Id.ChatId, cancellationToken).ConfigureAwait(false) != true)
            return false;

        // Pessimistically, consider that we require translation.
        return true;
    }

    public virtual async Task<Translation?> Get(TextEntryId entryId, CancellationToken cancellationToken = default)
        => await Get(TranslationSourceId.New(entryId), cancellationToken).ConfigureAwait(false);

    public virtual async Task<Translation?> Get(ThreadChatId threadChatId, ThreadTranslationIdKind kind, CancellationToken cancellationToken = default)
        => await Get(TranslationSourceId.New(threadChatId, kind), cancellationToken).ConfigureAwait(false);

    public virtual async Task<Translation?> Get(ConversationId conversationId, ConversationTranslationIdKind kind, CancellationToken cancellationToken = default)
        => await Get(TranslationSourceId.New(conversationId, kind), cancellationToken).ConfigureAwait(false);

    [ComputeMethod]
    public virtual async Task<Translation?> Get(TranslationSourceId translationSourceId, CancellationToken cancellationToken = default){
        var session = Session;
        var targetLanguage = await GetTargetLanguage(translationSourceId.ChatId, cancellationToken).ConfigureAwait(false);
        return await Translations.Get(session, TranslationId.New(translationSourceId, targetLanguage), cancellationToken).ConfigureAwait(false);
    }
	
   /*public async IAsyncEnumerable<TranscriptDiff> GetTranscript(ChatEntryId entryId, [EnumeratorCancellation] CancellationToken cancellationToken)
   {
       var entry = await ChatUI.GetEntry(entryId, cancellationToken).Require().ConfigureAwait(false);
        var targetLanguage = await GetTargetLanguage(entry.ChatId, cancellationToken).ConfigureAwait(false);
        var translationId = TranslationId.New(entry.Id.ToTextEntryId(), targetLanguage);
        var streamId = await GetStreamId(entry, cancellationToken).ConfigureAwait(false);
        if (streamId.IsEmpty)
            yield break;

        var translation = await Get(translationId.SourceId, cancellationToken).ConfigureAwait(false);
        // TODO(FC,AK): must be unified streaming api
        if (translation?.IsRealtime ?? true)
            await foreach(var diff in StreamClient.GetTranslatedTranscript(streamId, translationId, cancellationToken).ConfigureAwait(false))
                yield return diff;
        else
            await foreach(var diff in StreamClient.GetTranslation(streamId, cancellationToken).ConfigureAwait(false))
                yield return new TranscriptDiff(diff, LinearMapDiff.None);
   }*/

   public async IAsyncEnumerable<TranscriptDiff> GetTranscript(ChatEntry entry, [EnumeratorCancellation] CancellationToken cancellationToken) {
        var targetLanguage = await GetTargetLanguage(entry.ChatId, cancellationToken).ConfigureAwait(false);
        var streamId = entry.IsStreaming
            ? StreamId.New(StreamId.Parse(entry.StreamId), targetLanguage)
            : null;

        if (streamId == null) {
            var translation = await Get(entry.Id.ToTextEntryId(), cancellationToken).ConfigureAwait(false);
            if (translation == null || translation.StreamId.IsNullOrEmpty())
                yield break;

            streamId = StreamId.Parse(translation.StreamId);
        }

        var transcriptStream = StreamClient.GetTranscript(streamId.Value, cancellationToken);
        await foreach(var diff in transcriptStream.ConfigureAwait(false))
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
   protected virtual async Task<Symbol> GetStreamId(ChatEntry entry, CancellationToken cancellationToken)
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
