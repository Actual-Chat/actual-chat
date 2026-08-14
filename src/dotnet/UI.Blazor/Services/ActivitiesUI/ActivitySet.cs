namespace ActualChat.UI.Blazor.Services;

[Flags]
public enum ActivityServiceTypes
{
    None = 0,
    Playback = 1,
    Microphone = 2,
    Location = 4,
    DataSync = 8,
}

/// <summary>
/// The set of ongoing activities requiring foreground state, priority-ordered;
/// platform backends render <see cref="Primary"/> and keep alive what <see cref="GetServiceTypes"/> demands.
/// </summary>
public sealed class ActivitySet : IEquatable<ActivitySet>
{
    public static readonly ActivitySet Empty = new ([]);

    public IReadOnlyList<ActivityInfo> Activities { get; }
    public ActivityInfo? Primary => Activities.Count > 0 ? Activities[0] : null;
    public bool IsEmpty => Activities.Count == 0;

    public ActivitySet(IEnumerable<ActivityInfo> activities)
        => Activities = activities.OrderBy(x => x.Priority).ToArray();

    public bool Contains(ActivityKind kind)
        => Activities.Any(x => x.Kind == kind);

    public ActivityServiceTypes GetServiceTypes()
    {
        var types = ActivityServiceTypes.None;
        foreach (var activity in Activities) {
            types |= activity.Kind switch {
                ActivityKind.Recording or ActivityKind.Armed
                    => ActivityServiceTypes.Playback | ActivityServiceTypes.Microphone,
                ActivityKind.Replaying or ActivityKind.Listening => ActivityServiceTypes.Playback,
                ActivityKind.SharingLocation => ActivityServiceTypes.Location,
                ActivityKind.Uploading or ActivityKind.Downloading => ActivityServiceTypes.DataSync,
                _ => ActivityServiceTypes.None,
            };
        }
        return types;
    }

    public bool Equals(ActivitySet? other)
        => other is not null && Activities.SequenceEqual(other.Activities);

    public override bool Equals(object? obj) => Equals(obj as ActivitySet);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var activity in Activities)
            hash.Add(activity);
        return hash.ToHashCode();
    }
}
