using ActualChat.Kvas;

namespace ActualChat.Users.UnitTests;

public partial class StoredSettingsSerializationTest
{
    // Nerdbank.MessagePack serializers — the wire format actually used by RPC.
    // KvasSerializer wraps these with a 1-byte format marker for KVAS storage.
    private static readonly IByteSerializer NerdbankMessagePack = Serializers.MessagePack;
    private static readonly IByteSerializer NerdbankKeylessMessagePack = Serializers.KeylessMessagePack;

    // Cross-version compat (Legacy ↔ New) only works through MemoryPack here: the new types
    // carry [Key(N)] for Nerdbank's array-keyed wire (a property-name map otherwise), while the
    // Legacy copies don't, so a Nerdbank round-trip across the type boundary is shape-mismatched.
    // MemoryPack uses MemoryPackOrder positionally on both sides — same indices match.
    private static readonly KvasSerializer MemoryPackKvasSerializer = new() { PreferMemoryPack = true };

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
        // Legacy still carries slot 4 (IsVideoStreamingEnabled). New type reserves
        // but does not expose slot 4; version-tolerant deserialization must ignore it.
        var legacy = new LegacyUserAppSettings {
            Origin = "test",
            IsDataCollectionEnabled = true,
            AreExperimentalFeaturesEnabled = false,
            IsIncompleteUIEnabled = true,
            IsVideoStreamingEnabled = false,
        };

        using var buffer = MemoryPackKvasSerializer.Write(legacy);
        var bytes = buffer.WrittenMemory;
        var result = MemoryPackKvasSerializer.Read<UserAppSettings>(ref bytes);

        result.Origin.Should().Be(legacy.Origin);
        result.IsDataCollectionEnabled.Should().Be(legacy.IsDataCollectionEnabled);
        result.AreExperimentalFeaturesEnabled.Should().Be(legacy.AreExperimentalFeaturesEnabled);
        result.IsIncompleteUIEnabled.Should().Be(legacy.IsIncompleteUIEnabled);
    }

    [Fact]
    public void LegacyUserEmailsSettings_DeserializesAs_UserEmailsSettings()
    {
        var legacy = new LegacyUserEmailsSettings {
            Origin = "email-test",
            DigestTime = new TimeSpan(12, 30, 0),
            IsDigestEnabled = false,
        };

        using var buffer = MemoryPackKvasSerializer.Write(legacy);
        var bytes = buffer.WrittenMemory;
        var result = MemoryPackKvasSerializer.Read<UserEmailsSettings>(ref bytes);

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
        };

        using var buffer = MemoryPackKvasSerializer.Write(settings);
        var bytes = buffer.WrittenMemory;
        var result = MemoryPackKvasSerializer.Read<LegacyUserAppSettings>(ref bytes);

        result.Origin.Should().Be(settings.Origin);
        result.IsDataCollectionEnabled.Should().Be(settings.IsDataCollectionEnabled);
        result.AreExperimentalFeaturesEnabled.Should().Be(settings.AreExperimentalFeaturesEnabled);
        result.IsIncompleteUIEnabled.Should().Be(settings.IsIncompleteUIEnabled);
        // New type omits slot 4 — legacy picks up the default (null).
        result.IsVideoStreamingEnabled.Should().BeNull();
    }

    [Fact]
    public void UserEmailsSettings_DeserializesAs_LegacyUserEmailsSettings()
    {
        var settings = new UserEmailsSettings {
            Origin = "new-email-test",
            DigestTime = new TimeSpan(18, 0, 0),
            IsDigestEnabled = true,
        };

        using var buffer = MemoryPackKvasSerializer.Write(settings);
        var bytes = buffer.WrittenMemory;
        var result = MemoryPackKvasSerializer.Read<LegacyUserEmailsSettings>(ref bytes);

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
        };

        using var buffer = MemoryPackKvasSerializer.Write<StoredSettings>(settings);
        var bytes = buffer.WrittenMemory;
        var result = MemoryPackKvasSerializer.Read<StoredSettings>(ref bytes);

        result.Should().BeOfType<UserAppSettings>();
        var typed = (UserAppSettings)result!;
        typed.Origin.Should().Be(settings.Origin);
        typed.IsDataCollectionEnabled.Should().Be(settings.IsDataCollectionEnabled);
        typed.AreExperimentalFeaturesEnabled.Should().Be(settings.AreExperimentalFeaturesEnabled);
        typed.IsIncompleteUIEnabled.Should().Be(settings.IsIncompleteUIEnabled);
    }

    [Fact]
    public void UserEmailsSettings_UnionRoundTrip()
    {
        var settings = new UserEmailsSettings {
            Origin = "union-email-test",
            DigestTime = new TimeSpan(10, 0, 0),
            IsDigestEnabled = true,
        };

        using var buffer = MemoryPackKvasSerializer.Write<StoredSettings>(settings);
        var bytes = buffer.WrittenMemory;
        var result = MemoryPackKvasSerializer.Read<StoredSettings>(ref bytes);

        result.Should().BeOfType<UserEmailsSettings>();
        var typed = (UserEmailsSettings)result!;
        typed.Origin.Should().Be(settings.Origin);
        typed.DigestTime.Should().Be(settings.DigestTime);
        typed.IsDigestEnabled.Should().Be(settings.IsDigestEnabled);
    }

    // --- Nerdbank.MessagePack polymorphic round-trip ---
    //
    // RPC sends StoredSettings as the declared base type (e.g. IUserSettings.Get returns
    // StoredSettings). Nerdbank dispatches via [DerivedTypeShape] union tags. These tests
    // exercise that path directly — independently of KVAS, MemoryPack, or RPC framing.

    [Fact]
    public void UserAppSettings_AsBase_NerdbankRoundTrip()
        => AssertBaseTypeRoundTrip(new UserAppSettings {
            Origin = "nb-test",
            IsDataCollectionEnabled = true,
            AreExperimentalFeaturesEnabled = false,
            IsIncompleteUIEnabled = true,
        });

    [Fact]
    public void UserChatRecordingDetectedLanguage_AsBase_NerdbankRoundTrip()
        => AssertBaseTypeRoundTrip(new UserChatRecordingDetectedLanguage {
            Timestamp = new Moment(new DateTime(2026, 04, 23, 12, 34, 56, DateTimeKind.Utc)),
            ChatId = default,
            Language = null,
        });

    [Fact]
    public void UserNavbarSettings_AsBase_NerdbankRoundTrip()
        => AssertBaseTypeRoundTrip(new UserNavbarSettings {
            Origin = "nav-test",
            PinnedChats = [],
            PlacesOrder = [],
        });

    private static void AssertBaseTypeRoundTrip<T>(T value)
        where T : StoredSettings
    {
        // Keyed wire (msgpack6).
        using (var buffer = NerdbankMessagePack.Write<StoredSettings>(value)) {
            var bytes = buffer.WrittenMemory.ToArray();
            var read = (StoredSettings?)NerdbankMessagePack.Read(bytes, typeof(StoredSettings), out _);
            read.Should().BeOfType<T>();
            // Structural equivalence — records with array members default to reference equality
            // on the arrays, so two structurally identical instances aren't `Equals`.
            read.Should().BeEquivalentTo(value);
        }
        // Keyless wire (msgpack6k / msgpack6ck) — what TS-style maps look like.
        using (var buffer = NerdbankKeylessMessagePack.Write<StoredSettings>(value)) {
            var bytes = buffer.WrittenMemory.ToArray();
            var read = (StoredSettings?)NerdbankKeylessMessagePack.Read(bytes, typeof(StoredSettings), out _);
            read.Should().BeOfType<T>();
            read.Should().BeEquivalentTo(value);
        }
    }
}
