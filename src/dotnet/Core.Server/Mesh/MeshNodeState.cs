namespace ActualChat.Mesh;

public enum MeshNodeState
{
    Online = 0,
    Dead = 3,
}

public static class MeshNodeStateExt
{
    public static string FormatSuffix(this MeshNodeState state)
        => state switch {
            MeshNodeState.Online => "-online",
            MeshNodeState.Dead => "-dead",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
}
