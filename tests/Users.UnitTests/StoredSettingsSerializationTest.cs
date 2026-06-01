using ActualChat.Chat;
using ActualChat.Kvas;

namespace ActualChat.Users.UnitTests;

public partial class StoredSettingsSerializationTest
{
    // MessagePack-CSharp serializer — the wire format actually used by RPC.
    // KvasSerializer wraps this with a 1-byte format marker for KVAS storage.
    private static readonly IByteSerializer MessagePackSerializer = Serializers.MessagePack;

    // Cross-version compat (Legacy ↔ New) only works through MemoryPack here: the new types
    // carry [Key(N)] for MessagePack-CSharp's array-keyed wire, while the Legacy copies
    // don't, so a MessagePack round-trip across the type boundary is shape-mismatched.
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

        using var buffer = KvasSerializer.Default.Write<StoredSettings>(settings);
        var bytes = buffer.WrittenMemory;
        var result = KvasSerializer.Default.Read<StoredSettings>(ref bytes);

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

        using var buffer = KvasSerializer.Default.Write<StoredSettings>(settings);
        var bytes = buffer.WrittenMemory;
        var result = KvasSerializer.Default.Read<StoredSettings>(ref bytes);

        result.Should().BeOfType<UserEmailsSettings>();
        var typed = (UserEmailsSettings)result!;
        typed.Origin.Should().Be(settings.Origin);
        typed.DigestTime.Should().Be(settings.DigestTime);
        typed.IsDigestEnabled.Should().Be(settings.IsDigestEnabled);
    }

    // --- MessagePack-CSharp polymorphic round-trip ---
    //
    // RPC sends StoredSettings as the declared base type (e.g. IUserSettings.Get returns
    // StoredSettings). MessagePack-CSharp dispatches via [Union(N, typeof(Derived))] tags
    // declared on the base. These tests exercise that path directly — independently of
    // KVAS, MemoryPack, or RPC framing.

    [Fact]
    public void UserAppSettings_AsBase_MessagePackRoundTrip()
        => AssertBaseTypeRoundTrip(new UserAppSettings {
            Origin = "mp-test",
            IsDataCollectionEnabled = true,
            AreExperimentalFeaturesEnabled = false,
            IsIncompleteUIEnabled = true,
        });

    [Fact]
    public void UserChatRecordingDetectedLanguage_AsBase_MessagePackRoundTrip()
        => AssertBaseTypeRoundTrip(new UserChatRecordingDetectedLanguage {
            Timestamp = new Moment(new DateTime(2026, 04, 23, 12, 34, 56, DateTimeKind.Utc)),
            ChatId = default,
            Language = null,
        });

    [Fact]
    public void UserNavbarSettings_AsBase_MessagePackRoundTrip()
        => AssertBaseTypeRoundTrip(new UserNavbarSettings {
            Origin = "nav-test",
            PinnedChats = [],
            PlacesOrder = [],
        });

    // New MessagePack-only settings types (no MemoryPack) — verify the [Union] dispatch works.

    [Fact]
    public void RecentMentions_AsBase_MessagePackRoundTrip()
        => AssertBaseTypeRoundTrip(new RecentMentions {
            Origin = "recents",
            Items = [
                new RecentMention {
                    Id = MentionRef.NewUser(UserId.New()),
                    Uses = [new Moment(new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc))],
                },
            ],
        });

    [Fact]
    public void RecentGifs_AsBase_MessagePackRoundTrip()
        => AssertBaseTypeRoundTrip(new RecentGifs {
            Origin = "gifs",
            Items = [
                new RecentGif {
                    Slug = "party-parrot",
                    PreviewUrl = "https://example.com/p.gif",
                    PreviewWidth = 120,
                    PreviewHeight = 90,
                    Url = "https://example.com/f.gif",
                },
            ],
        });

    private static void AssertBaseTypeRoundTrip<T>(T value)
        where T : StoredSettings
    {
        using var buffer = MessagePackSerializer.Write<StoredSettings>(value);
        var bytes = buffer.WrittenMemory.ToArray();
        var read = (StoredSettings?)MessagePackSerializer.Read(bytes, typeof(StoredSettings), out _);
        read.Should().BeOfType<T>();
        // Structural equivalence — records with array members default to reference equality
        // on the arrays, so two structurally identical instances aren't `Equals`.
        read.Should().BeEquivalentTo(value);
    }
}
