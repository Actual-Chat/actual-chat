using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Hashing;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("CoachEntries")]
[Index(nameof(ChatId), nameof(AuthorId), nameof(LocalId))]
[Index(nameof(ChatId), nameof(TagState))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbCoachEntry : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string ChatId { get; set; } = "";
    public long LocalId { get; set; }
    public string AuthorId { get; set; } = "";
    public string UserId { get; set; } = "";

    public DateTime BeginsAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public string? Language { get; set; }
    public double DurationSeconds { get; set; }
    public double? SpeechSeconds { get; set; }
    public int? Words { get; set; }
    public int? Sentences { get; set; }
    public int? Questions { get; set; }
    public int? Repetitions { get; set; }
    public int? DistinctWords { get; set; }
    public int? Pauses { get; set; }
    public double? PauseSeconds { get; set; }
    public string Spans { get; set; } = "[]";
    public int FilledPauses { get; set; }
    public int Fillers { get; set; }
    public int WeakWords { get; set; }
    public int Profanities { get; set; }
    public CoachTagState TagState { get; set; }
    public int PromptVersion { get; set; }

    public DateTime? TaggedAt {
        get => field?.DefaultKind(DateTimeKind.Utc);
        set => field = value?.DefaultKind(DateTimeKind.Utc);
    }

    public string ContentHash { get; set; } = "";

    public DbCoachEntry() { }
    public DbCoachEntry(CoachEntryAnalysis model) => UpdateFrom(model);

    public CoachEntryAnalysis ToModel()
        => new (ChatEntryId.Parse(Id), Version) {
            AuthorId = ActualChat.AuthorId.Parse(AuthorId),
            UserId = ActualChat.UserId.Parse(UserId),
            BeginsAt = BeginsAt,
            Language = ActualChat.Language.ParseNullable(Language),
            DurationSeconds = DurationSeconds,
            SpeechSeconds = SpeechSeconds,
            Words = Words,
            Sentences = Sentences,
            Questions = Questions,
            Repetitions = Repetitions,
            DistinctWords = DistinctWords,
            Pauses = Pauses,
            PauseSeconds = PauseSeconds,
            Spans = JsonSerializer.Deserialize<SpeechSpan[]>(Spans)?.ToApiArray() ?? ApiArray<SpeechSpan>.Empty,
            FilledPauses = FilledPauses,
            Fillers = Fillers,
            WeakWords = WeakWords,
            Profanities = Profanities,
            TagState = TagState,
            PromptVersion = PromptVersion,
            TaggedAt = TaggedAt is { } taggedAt ? taggedAt : null,
            ContentHash = HashString.ParseOrNone(ContentHash),
        };

    public void UpdateFrom(CoachEntryAnalysis model)
    {
        this.RequireSameOrEmptyId(model.Id.Value);
        model.RequireVersion();

        Id = model.Id.Value;
        Version = model.Version;
        ChatId = model.Id.ChatId.Value;
        LocalId = model.Id.LocalId;
        AuthorId = model.AuthorId.Value;
        UserId = model.UserId.Value;
        BeginsAt = model.BeginsAt;
        Language = model.Language?.Value;
        DurationSeconds = model.DurationSeconds;
        SpeechSeconds = model.SpeechSeconds;
        Words = model.Words;
        Sentences = model.Sentences;
        Questions = model.Questions;
        Repetitions = model.Repetitions;
        DistinctWords = model.DistinctWords;
        Pauses = model.Pauses;
        PauseSeconds = model.PauseSeconds;
        Spans = JsonSerializer.Serialize(model.Spans.ToArray());
        FilledPauses = model.FilledPauses;
        Fillers = model.Fillers;
        WeakWords = model.WeakWords;
        Profanities = model.Profanities;
        TagState = model.TagState;
        PromptVersion = model.PromptVersion;
        TaggedAt = model.TaggedAt is { } taggedAt ? taggedAt : null;
        ContentHash = model.ContentHash.Value;
    }
}
