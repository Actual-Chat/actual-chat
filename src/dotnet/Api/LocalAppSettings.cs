using ActualChat.Kvas;
using ActualLab.Api;

namespace ActualChat;

/// <summary>
/// Application settings stored locally on the device.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
public partial record LocalAppSettings : StoredSettings, IHasKvasKey<LocalAppSettings>
{
    [DataMember, MemoryPackOrder(0), Key(0)] public bool? IsLogViewerEnabled { get; init; }
    [DataMember, MemoryPackOrder(1), Key(1)] public string? SelectedCameraDeviceId { get; init; }
    [DataMember, MemoryPackOrder(2), Key(2)] public bool? IsBackgroundBlurEnabled { get; init; }

    // MediaStreamTrack.getSettings().deviceId -> isMirrored.
    // `field ??=` handles older payloads where MemoryPack's version-tolerant
    // reader leaves the property null (init-only defaults don't run during
    // deserialization).
    [DataMember, MemoryPackOrder(3), Key(3)]
    public ApiMap<string, bool> CameraMirrorOverrides {
        get => field ??= ApiMap<string, bool>.Empty;
        init;
    } = ApiMap<string, bool>.Empty;

    [DataMember, MemoryPackOrder(4), Key(4)] public bool? IsVideoDiagnosticsEnabled { get; init; }
    [DataMember, MemoryPackOrder(5), Key(5)] public GeoTrackingAccuracy? LocationAccuracy { get; init; }
    [DataMember, MemoryPackOrder(6), Key(6)] public bool? IsAudioDiagnosticsEnabled { get; init; }

    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsLogViewerEnabledOrDefault => IsLogViewerEnabled ?? true;
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public bool IsBackgroundBlurEnabledOrDefault => IsBackgroundBlurEnabled ?? false;
    // Dev instances default to ON so the diagnostics entry points are visible
    // out of the box; production defaults to OFF until the user opts in.
    public bool IsVideoDiagnosticsEnabledOrDefault(bool isDevelopmentInstance)
        => IsVideoDiagnosticsEnabled ?? isDevelopmentInstance;
    public bool IsAudioDiagnosticsEnabledOrDefault(bool isDevelopmentInstance)
        => IsAudioDiagnosticsEnabled ?? isDevelopmentInstance;
    [IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public GeoTrackingAccuracy LocationAccuracyOrDefault => LocationAccuracy ?? GeoTrackingAccuracy.Balanced;
}

public static class LocalAppSettingsExt
{
    public static bool ResolveIsCameraMirrored(
        this LocalAppSettings settings, string? deviceId, string? facingMode, bool isMobile)
    {
        // Explicit user override wins; otherwise mirror selfie-style views —
        // every desktop camera, and mobile front ('user') cameras. Mobile back
        // cameras aren't mirrored (default would look wrong for "the real world").
        if (!string.IsNullOrEmpty(deviceId) && settings.CameraMirrorOverrides.TryGetValue(deviceId, out var v))
            return v;

        return !isMobile || facingMode == "user";
    }
}
