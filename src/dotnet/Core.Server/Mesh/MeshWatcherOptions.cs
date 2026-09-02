namespace ActualChat.Mesh;

public sealed record MeshWatcherOptions
{
    public static readonly MeshWatcherOptions Default = new();

    // Off only for a host that never starts, e.g. a mesh test host: it must still join the mesh
    public bool MustAnnounceAfterHostStart { get; init; } = true;
}
