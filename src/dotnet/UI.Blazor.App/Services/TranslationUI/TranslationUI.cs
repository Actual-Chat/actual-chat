using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public class TranslationUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    public ChatUI ChatUI => Hub.ChatUI;
    public LanguageUI LanguageUI => Hub.LanguageUI;
    public ITranslations Translations => Hub.Translations;

    [ComputeMethod]
    public virtual async Task<bool?> IsOn(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var userChatSettings = await AccountSettings.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        return userChatSettings.MustTranslate;
    }

    [ComputeMethod]
    public virtual async Task<Language> GetTargetLanguage(ChatId chatId, CancellationToken cancellationToken = default)
    {
        var userChatSettings = await AccountSettings.GetUserChatSettings(chatId, cancellationToken).ConfigureAwait(false);
        return userChatSettings.TranslationTargetLanguage ?? await LanguageUI.GetChatLanguage(chatId, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<bool> MustSuggest(ChatId chatId, CancellationToken cancellationToken = default){
        var isOn = await IsOn(chatId, cancellationToken).ConfigureAwait(false);
        if (isOn != null)
            return false;

        var itemVisibility = await ChatUI.ItemVisibility.Use(cancellationToken).ConfigureAwait(false);
        if (itemVisibility.IsEmpty || itemVisibility.ChatId != chatId)
            return false;

        var items = await itemVisibility.VisibleEntryIds.Select(id => NeedsTranslation(id, cancellationToken))
            .Collect(1, cancellationToken)
            .ConfigureAwait(false);
        return items.Any(x => x);
    }

    [ComputeMethod]
    public virtual async Task<bool> NeedsTranslation(TextEntryId entryId, CancellationToken cancellationToken = default)
    {
        var session = Session;
        var entryLanguage = await Translations.GetLanguage(session, entryId, cancellationToken).ConfigureAwait(false);
        if (entryLanguage == null)
            return false;

        var spokenLanguages = await LanguageUI.ListSpokenLanguages(cancellationToken).ConfigureAwait(false);
        return entryLanguage.Languages.Any(x => !spokenLanguages.Contains(x));
    }

    [ComputeMethod]
    public virtual async Task<Translation?> Get(TextEntryId entryId, CancellationToken cancellationToken = default){
        var session = Session;
        var targetLanguage = await GetTargetLanguage(entryId.ChatId, cancellationToken).ConfigureAwait(false);
        return await Translations.Get(session, TranslationId.New(entryId, targetLanguage), cancellationToken).ConfigureAwait(false);
    }

    public Task SetIsOn(ChatId chatId, bool? value, CancellationToken cancellationToken = default)
        => AccountSettings.UpdateUserChatSettings(chatId, x => x with { MustTranslate = value }, cancellationToken);

    public Task SetTargetLanguage(ChatId chatId, Language? language, CancellationToken cancellationToken = default)
        => AccountSettings.UpdateUserChatSettings(chatId, x => x with { TranslationTargetLanguage = language }, cancellationToken);
}
