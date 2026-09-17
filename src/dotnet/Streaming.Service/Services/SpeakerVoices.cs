using ActualChat.Chat;
using ActualChat.Users;

namespace ActualChat.Streaming.Services;

// The voice a speaker is dubbed with: VoicePool's clone when the speaker opted in and one is
// acquirable, otherwise the stock voice - UserLanguageSettings.DubVoice when the catalog lists it,
// otherwise the first voice DubVoiceAccents suggests for the speaker's languages ("Default" in the
// picker). Null means the synthesizer's default - guests, anonymous authors, an empty catalog, or any
// lookup failure: a voice is never worth failing a dub over
public sealed class SpeakerVoices(IServiceProvider services)
{
    private IAuthorsBackend AuthorsBackend { get; } = services.GetRequiredService<IAuthorsBackend>();
    private IServerKvasBackend ServerKvasBackend { get; } = services.GetRequiredService<IServerKvasBackend>();
    private ITranslationsBackend TranslationsBackend { get; } = services.GetRequiredService<ITranslationsBackend>();
    private VoicePool VoicePool { get; } = services.GetRequiredService<VoicePool>();
    private ILogger Log { get; } = services.LogFor<SpeakerVoices>();

    public async Task<string?> Get(ChatId chatId, AuthorId authorId, CancellationToken cancellationToken)
    {
        try {
            var (userId, settings) = await GetSettings(chatId, authorId, cancellationToken).ConfigureAwait(false);
            if (settings == null)
                return null;

            if (settings.IsOwnVoiceEnabled) {
                // A pool id is a Soniox UUID, not something the catalog would ever list - it skips
                // the ResolveVoice/catalog check below on purpose
                var cloneVoiceId = await VoicePool.Acquire(userId!, cancellationToken).ConfigureAwait(false);
                if (!cloneVoiceId.IsNullOrEmpty())
                    return cloneVoiceId;
            }

            var voices = await TranslationsBackend.ListDubVoices(cancellationToken).ConfigureAwait(false);
            var chosenVoiceId = settings.DubVoice;
            if (!chosenVoiceId.IsNullOrEmpty() && !voices.Any(x => x.Id == chosenVoiceId))
                Log.LogInformation("Author {AuthorId} picked voice '{VoiceId}', which the catalog doesn't list",
                    authorId, chosenVoiceId);
            return DubVoiceAccents.ResolveVoice(chosenVoiceId, voices, settings.ListSpoken());
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogInformation(e, "Failed to read the voice of author {AuthorId}, using the default", authorId);
            return null;
        }
    }

    // Private methods

    private async Task<(UserId? UserId, UserLanguageSettings? Settings)> GetSettings(
        ChatId chatId,
        AuthorId authorId,
        CancellationToken cancellationToken)
    {
        var author = await AuthorsBackend.Get(chatId, authorId, RequestedAuthorKind.Full, cancellationToken)
            .ConfigureAwait(false);
        if (author == null || author.UserId.IsGuestOrNull() || author.IsAnonymous)
            return (null, null);

        var settings = await ServerKvasBackend.ForUser(author.UserId)
            .UserLanguageSettings()
            .Get(cancellationToken)
            .ConfigureAwait(false);
        return (author.UserId, settings);
    }
}
