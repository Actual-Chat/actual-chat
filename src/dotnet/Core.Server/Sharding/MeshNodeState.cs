namespace ActualChat;

public enum MeshNodeState
{
    Online = 0,
    Offline,
    Dead,
}

public static class MeshNodeStateExt
{
    public static string FormatSuffix(this MeshNodeState state)
        => state switch {
            MeshNodeState.Online => "",
            MeshNodeState.Offline => "-offline",
            MeshNodeState.Dead => "-dead",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
}
