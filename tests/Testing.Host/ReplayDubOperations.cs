using ActualChat.Streaming.Services;

namespace ActualChat.Testing.Host;

public static class ReplayDubOperations
{
    public static async Task<Translation> WhenReplayDubStored(
        this IServiceProvider services,
        TranslationId id,
        CancellationToken cancellationToken,
        string voiceId = "")
    {
        // "Stored" means the whole run is over: the translation carries the dub and ReplayDubs has
        // dropped its in-flight entry, so the next GetOrCreate reads the media instead of joining the run
        var translations = services.GetRequiredService<ITranslationsBackend>();
        var dubs = services.GetRequiredService<ReplayDubs>();
        var translation = await ComputedTest.When(
            async ct => {
                var t = await translations.Get(id, translateIfMissing: false, ct).Require().ConfigureAwait(false);
                t.HasValidDub(voiceId).Should().BeTrue();
                return t;
            },
            TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        await TestExt.When(() => dubs.InFlightCount.Should().Be(0), TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        return translation;
    }
}
