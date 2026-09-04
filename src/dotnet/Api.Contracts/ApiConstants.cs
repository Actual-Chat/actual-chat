using ActualLab.Rpc.Serialization;

namespace ActualChat;

public static class ApiConstants
{
    public static readonly string VersionString = ThisAssembly.AssemblyVersion; // X.Y.0.0
    public static readonly string FullVersionString = typeof(ApiConstants).Assembly.GetInformationalVersion() ?? "0.0+unknown"; // X.Y.Z+123abc
    public static readonly string DisplayVersionString = "v" + FullVersionString.Replace('+', ' ');
    public static readonly Version Version = Version.Parse(VersionString);
    // X.Y.Z, i.e. the version the stores publish - unlike Version, which is always X.Y.0
    public static readonly Version BuildVersion = VersionExt.ParseBuildVersion(FullVersionString);

    public static class Concurrency
    {
        public static readonly int Unlimited = 0;
        public static readonly int High = 0; // Seemingly a better option than e.g., 256 or so
        public static readonly int Low = 128;
    }

    public static class Rpc
    {
        public const int MaxArgumentDataSize = 8 * 1024 * 1024;

        // Fusion's own frame limit is ~2x this, and a frame is buffered before the serializer
        // gets to reject it, so the transport is pinned to the largest message we can produce
        public static readonly int MaxMessageSize =
            RpcTextMessageSerializerV3.GetMaxMessageSize(MaxArgumentDataSize);
    }
}
