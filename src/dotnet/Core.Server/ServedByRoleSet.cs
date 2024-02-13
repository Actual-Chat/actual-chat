using ActualChat.Hosting;

namespace ActualChat;

public sealed class ServedByRoleSet
{
    private string? _toStringCache;

    public HostRole Backend { get; }
    public ImmutableArray<HostRole> Queues { get; }
    public IReadOnlySet<HostRole> AllRoles { get; }

    public ServedByRoleSet(IReadOnlySet<HostRole> allRoles)
    {
        Backend = allRoles.Single(x => !x.IsQueue);
        Queues = allRoles.Where(x => x.IsQueue).ToImmutableArray();
        AllRoles = allRoles;
    }

    public override string ToString()
        => _toStringCache ??= Queues.IsEmpty
            ? Backend.Value
            : $"{Backend} + [{Queues.ToDelimitedString()}]";
}
