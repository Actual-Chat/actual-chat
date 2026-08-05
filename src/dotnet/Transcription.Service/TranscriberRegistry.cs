using ActualChat.Transcription.Module;

namespace ActualChat.Transcription;

public sealed class TranscriberRegistry : ITranscriberRegistry
{
    private readonly Dictionary<TranscriberId, ITranscriber> _streams;
    private readonly Dictionary<TranscriberId, IOfflineTranscriber> _offlines;
    private readonly TranscriberId[] _streamRanking;
    private readonly TranscriberId[] _offlineRanking;
    private readonly Dictionary<Language, TranscriberId[]> _streamRankingOverrides;
    private readonly Dictionary<Language, TranscriberId[]> _offlineRankingOverrides;
    private readonly ConcurrentDictionary<TranscriberId, TranscriberInfo?> _pairInfos = new();

    public TranscriberRegistry(
        TranscriptionSettings settings,
        IEnumerable<ITranscriber> streams,
        IEnumerable<IOfflineTranscriber> offlines)
    {
        _streams = streams.ToDictionary(x => x.Info.Id);
        _offlines = offlines.ToDictionary(x => x.Info.Id);
        _streamRanking = ParseRanking(settings.UseFakeTranscriber ? FakeTranscriber.Id.Value : settings.StreamRanking);
        _offlineRanking = ParseRanking(settings.OfflineRanking);
        _streamRankingOverrides = ParseRankingOverrides(settings.StreamRankingOverrides);
        _offlineRankingOverrides = ParseRankingOverrides(settings.OfflineRankingOverrides);
    }

    public IReadOnlyList<ITranscriber> ListStream(TranscriptionOptions options, TranscriberId preferredId)
        => List(options,
            preferredId.PrimaryId,
            _streams,
            _streamRanking,
            _streamRankingOverrides,
            x => x.Info,
            x => x.Kind.IsStream());

    public IReadOnlyList<IOfflineTranscriber> ListOffline(TranscriptionOptions options, TranscriberId preferredId)
        => List(options,
            preferredId.RetranscriberId ?? TranscriberId.None,
            _offlines,
            _offlineRanking,
            _offlineRankingOverrides,
            x => x.Info,
            x => x.Kind.IsOffline());

    public ITranscriber? GetStream(TranscriberId id)
        => _streams.GetValueOrDefault(id.PrimaryId);

    public IOfflineTranscriber? GetOffline(TranscriberId id)
        => _offlines.GetValueOrDefault(id.IsPair ? id.RetranscriberId! : id);

    public TranscriberInfo? GetInfo(TranscriberId id)
        => id.IsPair
            ? _pairInfos.GetOrAdd(id, BuildPairInfo)
            : GetStream(id)?.Info ?? GetOffline(id)?.Info;

    // Private methods

    private TranscriberInfo? BuildPairInfo(TranscriberId id)
    {
        var primary = GetStream(id.PrimaryId);
        var retranscriber = _offlines.GetValueOrDefault(id.RetranscriberId!);
        if (primary == null || retranscriber == null || !retranscriber.Info.Kind.IsOffline())
            return null;

        var primaryInfo = primary.Info;
        var retranscriberInfo = retranscriber.Info;
        return primaryInfo with {
            Id = id,
            Retranscriber = retranscriberInfo,
            // A pair can only handle what both halves handle.
            Languages = Intersect(primaryInfo.Languages, retranscriberInfo.Languages),
            DetectLanguages = Intersect(primaryInfo.DetectLanguages, retranscriberInfo.DetectLanguages),
        };
    }

    private static ApiSet<Language> Intersect(ApiSet<Language> left, ApiSet<Language> right)
    {
        // An empty set declares no restriction rather than "nothing", so it can't narrow the other.
        if (left.Count == 0)
            return right;
        if (right.Count == 0)
            return left;

        return new ApiSet<Language>(left.Where(right.Contains));
    }

    private static List<T> List<T>(
        TranscriptionOptions options,
        TranscriberId preferredId,
        Dictionary<TranscriberId, T> registered,
        TranscriberId[] ranking,
        Dictionary<Language, TranscriberId[]> rankingOverrides,
        Func<T, TranscriberInfo> getInfo,
        Func<TranscriberInfo, bool> isRightKind)
        where T : class
    {
        if (!preferredId.IsNone) {
            // Falling back past an explicit pin would silently transcribe with something else.
            var preferred = registered.GetValueOrDefault(preferredId);
            return preferred != null && isRightKind(getInfo(preferred)) ? [preferred] : [];
        }

        var ids = rankingOverrides.GetValueOrDefault(options.Language) ?? ranking;
        var result = new List<T>(ids.Length);
        foreach (var id in ids) {
            var transcriber = registered.GetValueOrDefault(id);
            if (transcriber == null)
                continue;

            var info = getInfo(transcriber);
            if (!isRightKind(info) || !IsUsable(info, options))
                continue;

            result.Add(transcriber);
        }

        return result;
    }

    private static bool IsUsable(TranscriberInfo info, TranscriptionOptions options)
        => options.DetectLanguage
            ? info.IsDetectionSupported(options.LanguageCandidates)
            : info.IsSupported(options.Language);

    private static TranscriberId[] ParseRanking(string ranking)
    {
        var parts = ranking.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var result = new List<TranscriberId>(parts.Length);
        foreach (var part in parts) {
            var id = TranscriberId.TryParse(part);
            if (id != null)
                result.Add(id);
        }

        return result.ToArray();
    }

    private static Dictionary<Language, TranscriberId[]> ParseRankingOverrides(Dictionary<string, string> overrides)
    {
        var result = new Dictionary<Language, TranscriberId[]>();
        foreach (var (languageId, ranking) in overrides) {
            var language = Language.TryParse(languageId);
            if (language == null)
                continue;

            result[language] = ParseRanking(ranking);
        }

        return result;
    }
}
