using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Components;

public sealed record CoachFinding(CoachMetricKind Kind, CoachBand Band);

public static class CoachFindings
{
    private const int Count = 3;

    // Problems first, then the focus skill, then good news, then whatever else has data
    public static IReadOnlyList<CoachFinding> Pick(
        CoachConversation c, CoachMetricKind? focus, CoachLanguageLevel level, bool isWeeksBest)
    {
        var all = new List<CoachFinding> {
            new (CoachMetricKind.Fillers, c.FillerBand),
            new (CoachMetricKind.Pace, c.PaceBand),
            new (CoachMetricKind.TurnTaking, c.TalkShareBand),
            new (CoachMetricKind.Monologue, c.MonologueBand),
        };
        var order = CoachSkillSets.Headline(level).Concat(all.Select(f => f.Kind)).Distinct().ToList();
        var available = all
            .Where(f => f.Band != CoachBand.None)
            .Where(f => f.Kind != CoachMetricKind.TurnTaking || c.Participants >= 2)
            .OrderBy(f => order.IndexOf(f.Kind))
            .ToList();
        var picked = new List<CoachFinding>();
        Take(available.Where(f => f.Band is CoachBand.High or CoachBand.Low));
        Take(available.Where(f => f.Kind == focus));
        Take(available.Where(f => f.Band == CoachBand.Good));
        Take(available);
        return picked;

        void Take(IEnumerable<CoachFinding> source) {
            foreach (var f in source)
                if (picked.Count < Count && picked.All(p => p.Kind != f.Kind))
                    picked.Add(f);
        }
    }
}
