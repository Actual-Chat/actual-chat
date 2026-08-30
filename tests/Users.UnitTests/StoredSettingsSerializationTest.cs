using ActualChat.Kvas;

namespace ActualChat.Users.UnitTests;

public partial class StoredSettingsSerializationTest
{
    // MessagePack-CSharp serializer — the wire format actually used by RPC.
    // KvasSerializer wraps this with a 1-byte format marker for KVAS storage.
    private static readonly IByteSerializer MessagePackSerializer = Serializers.MessagePack;

    // Cross-version compat (Legacy ↔ New) works through MemoryPack for the older Legacy
    // copies below: they carry MemoryPackOrder only, so a MessagePack round-trip across
    // the type boundary is shape-mismatched. Legacy copies that also carry [Key(N)]
    // (e.g. LegacyUserListeningSettings) cover the MessagePack wire direction too.
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

    [DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
    public sealed partial record LegacyUserListeningSettings
    {
        [DataMember, MemoryPackOrder(0), Key(0)] public ChatId[] AlwaysListenedChatIds { get; init; } = [];
        [DataMember, MemoryPackOrder(1), Key(1)] public string Origin { get; init; } = "";
    }

    // Declares only the old ListeningMode slot; MessagePack's array format skips the
    // undeclared indexes on read, so this checks just what old clients see in slot 3.
    [MessagePackObject]
    public sealed partial record LegacyChatUserSettingsListeningModeSlot
    {
        [Key(3)] public int ListeningMode { get; init; }
    }

    // --- Legacy → New compatibility ---

    [Fact]
    public void LegacyUserAppSettingsDeserializesAsUserAppSettings()
    {
        // arrange
        // Legacy still carries slot 4 (IsVideoStreamingEnabled). New type reserves
        // but does not expose slot 4; version-tolerant deserialization must ignore it.
        var legacy = new LegacyUserAppSettings {
            Origin = "test",
            IsDataCollectionEnabled = true,
            AreExperimentalFeaturesEnabled = false,
            IsIncompleteUIEnabled = true,
            IsVideoStreamingEnabled = false,
        };

        // act
        using var buffer = MemoryPackKvasSerializer.Write(legacy);
        var bytes = buffer.WrittenMemory;
        var result = MemoryPackKvasSerializer.Read<UserAppSettings>(ref bytes);

        // assert
        result.Origin.Should().Be(legacy.Origin);
        result.IsDataCollectionEnabled.Should().Be(legacy.IsDataCollectionEnabled);
        result.AreExperimentalFeaturesEnabled.Should().Be(legacy.AreExperimentalFeaturesEnabled);
        result.IsIncompleteUIEnabled.Should().Be(legacy.IsIncompleteUIEnabled);
    }

    [Fact]
    public void LegacyUserEmailsSettingsDeserializesAsUserEmailsSettings()
    {
        // arrange
        var legacy = new LegacyUserEmailsSettings {
            Origin = "email-test",
            DigestTime = new TimeSpan(12, 30, 0),
            IsDigestEnabled = false,
        };

        // act
        using var buffer = MemoryPackKvasSerializer.Write(legacy);
        var bytes = buffer.WrittenMemory;
        var result = MemoryPackKvasSerializer.Read<UserEmailsSettings>(ref bytes);

        // assert
        result.Origin.Should().Be(legacy.Origin);
        result.DigestTime.Should().Be(legacy.DigestTime);
        result.IsDigestEnabled.Should().Be(legacy.IsDigestEnabled);
    }

    [Fact]
    public void LegacyUserListeningSettingsDeserializesAsUserListeningSettings()
    {
        // arrange
        // Legacy has no slot 2 (ListeningLinger) — it must read as the default.
        var legacy = new LegacyUserListeningSettings {
            AlwaysListenedChatIds = [ChatId.Parse("the-actual-one")],
            Origin = "listening-test",
        };

        // act
        using var buffer = MemoryPackKvasSerializer.Write(legacy);
        var bytes = buffer.WrittenMemory;
        var result = MemoryPackKvasSerializer.Read<UserListeningSettings>(ref bytes);

        // assert
        result.Origin.Should().Be(legacy.Origin);
        result.ListeningLinger.Should().Be(ListeningLinger.None);
    }

    // --- New → Legacy compatibility ---

    [Fact]
    public void UserAppSettingsDeserializesAsLegacyUserAppSettings()
    {
        // arrange
        var settings = new UserAppSettings {
            Origin = "new-test",
            IsDataCollectionEnabled = false,
            AreExperimentalFeaturesEnabled = true,
            IsIncompleteUIEnabled = false,
        };

        // act
        using var buffer = MemoryPackKvasSerializer.Write(settings);
        var bytes = buffer.WrittenMemory;
        var result = MemoryPackKvasSerializer.Read<LegacyUserAppSettings>(ref bytes);

        // assert
        result.Origin.Should().Be(settings.Origin);
        result.IsDataCollectionEnabled.Should().Be(settings.IsDataCollectionEnabled);
        result.AreExperimentalFeaturesEnabled.Should().Be(settings.AreExperimentalFeaturesEnabled);
        result.IsIncompleteUIEnabled.Should().Be(settings.IsIncompleteUIEnabled);
        // New type omits slot 4 — legacy picks up the default (null).
        result.IsVideoStreamingEnabled.Should().BeNull();
    }

    [Fact]
    public void UserEmailsSettingsDeserializesAsLegacyUserEmailsSettings()
    {
        // arrange
        var settings = new UserEmailsSettings {
            Origin = "new-email-test",
            DigestTime = new TimeSpan(18, 0, 0),
            IsDigestEnabled = true,
        };

        // act
        using var buffer = MemoryPackKvasSerializer.Write(settings);
        var bytes = buffer.WrittenMemory;
        var result = MemoryPackKvasSerializer.Read<LegacyUserEmailsSettings>(ref bytes);

        // assert
        result.Origin.Should().Be(settings.Origin);
        result.DigestTime.Should().Be(settings.DigestTime);
        result.IsDigestEnabled.Should().Be(settings.IsDigestEnabled);
    }

    [Fact]
    public void UserListeningSettingsDeserializesAsLegacyUserListeningSettings()
    {
        // arrange
        var settings = new UserListeningSettings {
            Origin = "new-listening-test",
            ListeningLinger = ListeningLinger.For30Seconds,
        };

        // act
        using var buffer = MemoryPackKvasSerializer.Write(settings);
        var bytes = buffer.WrittenMemory;
        var result = MemoryPackKvasSerializer.Read<LegacyUserListeningSettings>(ref bytes);

        // assert
        result.Origin.Should().Be(settings.Origin);
        // The write-only [Obsolete] stub still fills slot 0, so old clients read [], never nil.
        // If the stub is ever removed and the slot reserved, this assertion must flip.
        result.AlwaysListenedChatIds.Should().NotBeNull();
        result.AlwaysListenedChatIds.Should().BeEmpty();
    }

    // --- New → Legacy compatibility, MessagePack (the production wire format) ---
    //
    // The write-only [Obsolete] stubs exist for the MessagePack nil problem, so the
    // pairs whose Legacy copies carry [Key(N)] verify that direction on the real wire.

    [Fact]
    public void UserListeningSettingsDeserializesAsLegacyViaMessagePack()
    {
        // arrange
        var settings = new UserListeningSettings {
            Origin = "mp-listening-test",
            ListeningLinger = ListeningLinger.For30Seconds,
        };

        // act
        using var buffer = MessagePackSerializer.Write(settings);
        var bytes = buffer.WrittenMemory.ToArray();
        var result = (LegacyUserListeningSettings?)MessagePackSerializer
            .Read(bytes, typeof(LegacyUserListeningSettings), out _);

        // assert
        result!.Origin.Should().Be(settings.Origin);
        // The stub fills slot 0 on the wire — old clients read [], never nil/null.
        result.AlwaysListenedChatIds.Should().NotBeNull();
        result.AlwaysListenedChatIds.Should().BeEmpty();
    }

    [Fact]
    public void ChatUserSettingsKeepsListeningModeSlotViaMessagePack()
    {
        // arrange
        var settings = new Chat.ChatUserSettings {
            NotificationMode = ChatNotificationMode.ImportantOnly,
            VoiceMode = VoiceMode.JustVoice,
        };

        // act
        using var buffer = MessagePackSerializer.Write(settings);
        var bytes = buffer.WrittenMemory.ToArray();
        var result = (LegacyChatUserSettingsListeningModeSlot?)MessagePackSerializer
            .Read(bytes, typeof(LegacyChatUserSettingsListeningModeSlot), out _);

        // assert
        // Old clients ReadInt32() this slot; without the write-only stub it's nil and this throws.
        result!.ListeningMode.Should().Be(0);
    }

    // --- Concrete type round-trip ---

    [Fact]
    public void UserAppSettingsRoundTrip()
    {
        // arrange
        var settings = new UserAppSettings {
            Origin = "round-trip",
            IsDataCollectionEnabled = true,
            AreExperimentalFeaturesEnabled = true,
            IsIncompleteUIEnabled = false,
        };

        // act
        using var buffer = KvasSerializer.Default.Write(settings);
        var bytes = buffer.WrittenMemory;
        var result = KvasSerializer.Default.Read<UserAppSettings>(ref bytes);

        // assert
        result.Should().Be(settings);
    }

    [Fact]
    public void UserEmailsSettingsRoundTrip()
    {
        // arrange
        var settings = new UserEmailsSettings {
            Origin = "round-trip",
            DigestTime = new TimeSpan(7, 15, 0),
            IsDigestEnabled = false,
        };

        // act
        using var buffer = KvasSerializer.Default.Write(settings);
        var bytes = buffer.WrittenMemory;
        var result = KvasSerializer.Default.Read<UserEmailsSettings>(ref bytes);

        // assert
        result.Should().Be(settings);
    }

    // --- Union (polymorphic) round-trip ---

    [Fact]
    public void UserAppSettingsUnionRoundTrip()
    {
        // arrange
        var settings = new UserAppSettings {
            Origin = "union-test",
            IsDataCollectionEnabled = true,
            AreExperimentalFeaturesEnabled = false,
            IsIncompleteUIEnabled = true,
        };

        // act
        using var buffer = KvasSerializer.Default.Write<StoredSettings>(settings);
        var bytes = buffer.WrittenMemory;
        var result = KvasSerializer.Default.Read<StoredSettings>(ref bytes);

        // assert
        result.Should().BeOfType<UserAppSettings>();
        var typed = (UserAppSettings)result!;
        typed.Origin.Should().Be(settings.Origin);
        typed.IsDataCollectionEnabled.Should().Be(settings.IsDataCollectionEnabled);
        typed.AreExperimentalFeaturesEnabled.Should().Be(settings.AreExperimentalFeaturesEnabled);
        typed.IsIncompleteUIEnabled.Should().Be(settings.IsIncompleteUIEnabled);
    }

    [Fact]
    public void UserEmailsSettingsUnionRoundTrip()
    {
        // arrange
        var settings = new UserEmailsSettings {
            Origin = "union-email-test",
            DigestTime = new TimeSpan(10, 0, 0),
            IsDigestEnabled = true,
        };

        // act
        using var buffer = KvasSerializer.Default.Write<StoredSettings>(settings);
        var bytes = buffer.WrittenMemory;
        var result = KvasSerializer.Default.Read<StoredSettings>(ref bytes);

        // assert
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
    public void UserAppSettingsAsBaseMessagePackRoundTrip()
        => AssertBaseTypeRoundTrip(new UserAppSettings {
            Origin = "mp-test",
            IsDataCollectionEnabled = true,
            AreExperimentalFeaturesEnabled = false,
            IsIncompleteUIEnabled = true,
        });

    [Fact]
    public void UserChatRecordingDetectedLanguageAsBaseMessagePackRoundTrip()
        => AssertBaseTypeRoundTrip(new UserChatRecordingDetectedLanguage {
            Timestamp = new Moment(new DateTime(2026, 04, 23, 12, 34, 56, DateTimeKind.Utc)),
            ChatId = default,
            Language = null,
        });

    [Fact]
    public void UserNavbarSettingsAsBaseMessagePackRoundTrip()
        => AssertBaseTypeRoundTrip(new UserNavbarSettings {
            Origin = "nav-test",
            PinnedChats = [],
            PlacesOrder = [],
        });

    // New MessagePack-only settings types (no MemoryPack) — verify the [Union] dispatch works.

    [Fact]
    public void RecentMentionsAsBaseMessagePackRoundTrip()
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
    public void RecentGifsAsBaseMessagePackRoundTrip()
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

    [Fact]
    public void UserCarAudioSettingsUnionRoundTrip()
    {
        // arrange
        var settings = new UserCarAudioSettings {
            Origin = "union-car-audio-test",
            Microphone = CarAudioDevice.Phone,
            Output = CarAudioDevice.Car,
        };

        // act
        using var buffer = KvasSerializer.Default.Write<StoredSettings>(settings);
        var bytes = buffer.WrittenMemory;
        var result = KvasSerializer.Default.Read<StoredSettings>(ref bytes);

        // assert
        result.Should().BeOfType<UserCarAudioSettings>();
        var typed = (UserCarAudioSettings)result!;
        typed.Origin.Should().Be(settings.Origin);
        typed.Microphone.Should().Be(CarAudioDevice.Phone);
        typed.Output.Should().Be(CarAudioDevice.Car);
    }

    [Fact]
    public void UserCarAudioSettingsShouldDefaultToAuto()
    {
        // arrange & act
        var settings = new UserCarAudioSettings();

        // assert
        settings.Microphone.Should().Be(CarAudioDevice.Auto);
        settings.Output.Should().Be(CarAudioDevice.Auto);
    }

    private static void AssertBaseTypeRoundTrip<T>(T value)
        where T : StoredSettings
    {
        // act
        using var buffer = MessagePackSerializer.Write<StoredSettings>(value);
        var bytes = buffer.WrittenMemory.ToArray();
        var read = (StoredSettings?)MessagePackSerializer.Read(bytes, typeof(StoredSettings), out _);

        // assert
        read.Should().BeOfType<T>();
        // Structural equivalence — records with array members default to reference equality
        // on the arrays, so two structurally identical instances aren't `Equals`.
        read.Should().BeEquivalentTo(value);
    }
}
