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
    public static string FormatSuffix(this MeshNodeState state)
        => state switch {
            MeshNodeState.Unknown => "-unknown",
            MeshNodeState.Online => "-online",
            MeshNodeState.Offline => "-offline",
            MeshNodeState.Dead => "-dead",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };

    public static bool IsLive(this MeshNodeState state)
        => state is not MeshNodeState.Dead;

    public static bool IsLive(this MeshNodeState? state)
        => state is not MeshNodeState.Dead;

    public static MeshNodeState OrUnknown(this MeshNodeState? state)
        => state ?? MeshNodeState.Unknown;
}
