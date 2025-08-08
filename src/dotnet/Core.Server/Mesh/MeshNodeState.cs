namespace ActualChat.Mesh;

public enum MeshNodeState
{
    Unknown = 0,
    Online,
    Offline,
    Dead,
}

public static class MeshNodeStateExt
{
    public static MeshNodeState Normalize(this MeshNodeState state)
        => state == MeshNodeState.Offline ? MeshNodeState.Online : state;

    public static MeshNodeState Normalize(this MeshNodeState state, bool normalize)
        => normalize
            ? state == MeshNodeState.Offline ? MeshNodeState.Online : state
            : state;

    public static string FormatSuffix(this MeshNodeState state)
        => state switch {
            MeshNodeState.Unknown => "-unknown",
            MeshNodeState.Online => "-online",
            MeshNodeState.Offline => "-offline",
            MeshNodeState.Dead => "-dead",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
}
