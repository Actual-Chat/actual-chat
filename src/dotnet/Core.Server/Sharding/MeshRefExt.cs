namespace ActualChat;

public static class MeshRefExt
{
    public static MeshRef RequireValid(this MeshRef meshRef)
        => meshRef.IsValid ? meshRef
            : throw new ArgumentOutOfRangeException(nameof(meshRef), $"Invalid {nameof(MeshRef)}: {meshRef}.");
}
