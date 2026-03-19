using ActualChat.Kvas;
using MemoryPack;

namespace ActualChat.Users.UnitTests;

public partial class StoredSettingsSerializationTest
{
    // Legacy types: exact copies of the original types without StoredSettings base.
    // Used to verify that adding the base record doesn't break binary compatibility.

    [DataContract, MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial record LegacyUserAppSettings
    {
        [DataMember, MemoryPackOrder(1)] public string Origin { get; init; } = "";
        [DataMember, MemoryPackOrder(0)] public bool? IsDataCollectionEnabled { get; init; }
        [DataMember, MemoryPackOrder(2)] public bool? AreExperimentalFeaturesEnabled { get; init; }
        [DataMember, MemoryPackOrder(3)] public bool? IsIncompleteUIEnabled { get; init; }
        [DataMember, MemoryPackOrder(4)] public bool? IsVideoStreamingEnabled { get; init; }
    }

    [DataContract, MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial record LegacyUserEmailsSettings
    {
        [DataMember, MemoryPackOrder(0)] public string Origin { get; init; } = "";
        [DataMember, MemoryPackOrder(1)] public TimeSpan DigestTime { get; init; } = new(9, 0, 0);
        [DataMember, MemoryPackOrder(2)] public bool IsDigestEnabled { get; init; } = true;
    }

    // --- Legacy → New compatibility ---

    [Fact]
    public void LegacyUserAppSettings_DeserializesAs_UserAppSettings()
    {
        var legacy = new LegacyUserAppSettings {
            Origin = "test",
            IsDataCollectionEnabled = true,
            AreExperimentalFeaturesEnabled = false,
            IsIncompleteUIEnabled = true,
            IsVideoStreamingEnabled = false,
        };

        using var buffer = KvasSerializer.Default.Write(legacy);
        var bytes = buffer.WrittenMemory;
        var result = KvasSerializer.Default.Read<UserAppSettings>(ref bytes);

        result.Origin.Should().Be(legacy.Origin);
        result.IsDataCollectionEnabled.Should().Be(legacy.IsDataCollectionEnabled);
        result.AreExperimentalFeaturesEnabled.Should().Be(legacy.AreExperimentalFeaturesEnabled);
        result.IsIncompleteUIEnabled.Should().Be(legacy.IsIncompleteUIEnabled);
        result.IsVideoStreamingEnabled.Should().Be(legacy.IsVideoStreamingEnabled);
    }

    [Fact]
    public void LegacyUserEmailsSettings_DeserializesAs_UserEmailsSettings()
    {
        var legacy = new LegacyUserEmailsSettings {
            Origin = "email-test",
            DigestTime = new TimeSpan(12, 30, 0),
            IsDigestEnabled = false,
        };

        using var buffer = KvasSerializer.Default.Write(legacy);
        var bytes = buffer.WrittenMemory;
        var result = KvasSerializer.Default.Read<UserEmailsSettings>(ref bytes);

        result.Origin.Should().Be(legacy.Origin);
        result.DigestTime.Should().Be(legacy.DigestTime);
        result.IsDigestEnabled.Should().Be(legacy.IsDigestEnabled);
    }

    // --- New → Legacy compatibility ---

    [Fact]
    public void UserAppSettings_DeserializesAs_LegacyUserAppSettings()
    {
        var settings = new UserAppSettings {
            Origin = "new-test",
            IsDataCollectionEnabled = false,
            AreExperimentalFeaturesEnabled = true,
            IsIncompleteUIEnabled = false,
            IsVideoStreamingEnabled = true,
        };

        using var buffer = KvasSerializer.Default.Write(settings);
        var bytes = buffer.WrittenMemory;
        var result = KvasSerializer.Default.Read<LegacyUserAppSettings>(ref bytes);

        result.Origin.Should().Be(settings.Origin);
        result.IsDataCollectionEnabled.Should().Be(settings.IsDataCollectionEnabled);
        result.AreExperimentalFeaturesEnabled.Should().Be(settings.AreExperimentalFeaturesEnabled);
        result.IsIncompleteUIEnabled.Should().Be(settings.IsIncompleteUIEnabled);
        result.IsVideoStreamingEnabled.Should().Be(settings.IsVideoStreamingEnabled);
    }

    [Fact]
    public void UserEmailsSettings_DeserializesAs_LegacyUserEmailsSettings()
    {
        var settings = new UserEmailsSettings {
            Origin = "new-email-test",
            DigestTime = new TimeSpan(18, 0, 0),
            IsDigestEnabled = true,
        };

        using var buffer = KvasSerializer.Default.Write(settings);
        var bytes = buffer.WrittenMemory;
        var result = KvasSerializer.Default.Read<UserEmailsSettings>(ref bytes);

        result.Origin.Should().Be(settings.Origin);
        result.DigestTime.Should().Be(settings.DigestTime);
        result.IsDigestEnabled.Should().Be(settings.IsDigestEnabled);
    }

    // --- Concrete type round-trip ---

    [Fact]
    public void UserAppSettings_RoundTrip()
    {
        var settings = new UserAppSettings {
            Origin = "round-trip",
            IsDataCollectionEnabled = true,
            AreExperimentalFeaturesEnabled = true,
            IsIncompleteUIEnabled = false,
            IsVideoStreamingEnabled = true,
        };

        using var buffer = KvasSerializer.Default.Write(settings);
        var bytes = buffer.WrittenMemory;
        var result = KvasSerializer.Default.Read<UserAppSettings>(ref bytes);

        result.Should().Be(settings);
    }

    [Fact]
    public void UserEmailsSettings_RoundTrip()
    {
        var settings = new UserEmailsSettings {
            Origin = "round-trip",
            DigestTime = new TimeSpan(7, 15, 0),
            IsDigestEnabled = false,
        };

        using var buffer = KvasSerializer.Default.Write(settings);
        var bytes = buffer.WrittenMemory;
        var result = KvasSerializer.Default.Read<UserEmailsSettings>(ref bytes);

        result.Should().Be(settings);
    }

    // --- Union (polymorphic) round-trip ---

    [Fact]
    public void UserAppSettings_UnionRoundTrip()
    {
        var settings = new UserAppSettings {
            Origin = "union-test",
            IsDataCollectionEnabled = true,
            AreExperimentalFeaturesEnabled = false,
            IsIncompleteUIEnabled = true,
            IsVideoStreamingEnabled = false,
        };

        using var buffer = KvasSerializer.Default.Write<StoredSettings>(settings);
        var bytes = buffer.WrittenMemory;
        var result = KvasSerializer.Default.Read<StoredSettings>(ref bytes);

        result.Should().BeOfType<UserAppSettings>();
        var typed = (UserAppSettings)result!;
        typed.Origin.Should().Be(settings.Origin);
        typed.IsDataCollectionEnabled.Should().Be(settings.IsDataCollectionEnabled);
        typed.AreExperimentalFeaturesEnabled.Should().Be(settings.AreExperimentalFeaturesEnabled);
        typed.IsIncompleteUIEnabled.Should().Be(settings.IsIncompleteUIEnabled);
        typed.IsVideoStreamingEnabled.Should().Be(settings.IsVideoStreamingEnabled);
    }

    [Fact]
    public void UserEmailsSettings_UnionRoundTrip()
    {
        var settings = new UserEmailsSettings {
            Origin = "union-email-test",
            DigestTime = new TimeSpan(10, 0, 0),
            IsDigestEnabled = true,
        };

        using var buffer = KvasSerializer.Default.Write<StoredSettings>(settings);
        var bytes = buffer.WrittenMemory;
        var result = KvasSerializer.Default.Read<StoredSettings>(ref bytes);

        result.Should().BeOfType<UserEmailsSettings>();
        var typed = (UserEmailsSettings)result!;
        typed.Origin.Should().Be(settings.Origin);
        typed.DigestTime.Should().Be(settings.DigestTime);
        typed.IsDigestEnabled.Should().Be(settings.IsDigestEnabled);
    }
}
