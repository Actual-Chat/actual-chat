namespace ActualChat.Mesh;

public enum MeshNodeState
{
    Online = 0,
    Offline = 1,
    Dead = 3,
}

public static class MeshNodeStateExt
{
    public static string FormatSuffix(this MeshNodeState state)
        => state switch {
            MeshNodeState.Offline => "-offline",
            MeshNodeState.Online => "-online",
            MeshNodeState.Dead => "-dead",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

    public static bool IsLive(this MeshNodeState state)
        => state is not MeshNodeState.Dead;

    public static bool IsLive(this MeshNodeState? state)
        => state is not MeshNodeState.Dead;

    public static bool Equals(this MeshNodeState state, MeshNodeState other, bool simplifyToLiveOrDead = false)
        => simplifyToLiveOrDead
            ? state.IsLive() == other.IsLive()
            : state == other;

    public static bool Equals(this MeshNodeState? state, MeshNodeState? other, bool simplifyToLiveOrDead = false)
        => simplifyToLiveOrDead
            ? state.IsLive() == other.IsLive()
            : state == other;
}
