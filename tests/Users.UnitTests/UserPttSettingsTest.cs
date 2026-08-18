using ActualChat.Kvas;

namespace ActualChat.Users.UnitTests;

public partial class UserPttSettingsTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public void DefaultsAreSafe()
    {
        var settings = new UserPttSettings();
        // act + assert
        settings.PttChatIds.Should().BeEmpty();
        settings.IsFlipToTalkEnabled.Should().BeTrue();
        settings.IsDoubleShakeEnabled.Should().BeTrue();
        settings.ShakeSensitivity.Should().Be(ShakeSensitivity.Medium);
        settings.AreGesturesAlwaysOn.Should().BeFalse();
        settings.HotWindow.Should().Be(TimeSpan.FromSeconds(60));
        settings.AreAudibleCuesEnabled.Should().BeTrue();
        (settings.IsHeadsetButtonEnabled ?? true).Should().BeTrue();
    }

    [Fact]
    public void ABlobPredatingTheHeadsetButtonReadsAsEnabled()
    {
        // arrange
        var e2Era = new E2UserPttSettings {
            PttChatIds = [TestChatId],
            Origin = "test",
            AreAudibleCuesEnabled = false,
        };

        // act
        using var buffer = KvasSerializer.Default.Write(e2Era);
        var bytes = buffer.WrittenMemory;
        var settings = KvasSerializer.Default.Read<UserPttSettings>(ref bytes);

        // assert
        settings.PttChatIds.Should().Equal(e2Era.PttChatIds);
        settings.PttChats.Should().BeEmpty("the member is absent from the blob");
        settings.AllPttChats.Should().Equal(new PttChat(TestChatId, default));
        settings.AreAudibleCuesEnabled.Should().BeFalse();
        settings.IsHeadsetButtonEnabled.Should().BeNull("the member is absent from the blob");
        (settings.IsHeadsetButtonEnabled ?? true).Should().BeTrue("read sites must default it to on");
    }

    [Fact]
    public void AMissingMemberIgnoresItsPropertyInitializer()
    {
        // Why IsHeadsetButtonEnabled has to be bool?: a member absent from the blob deserializes
        // as default(T), so a plain `bool ... = true` silently reads as disabled.

        // arrange
        var truncated = new TruncatedUserPttSettings { PttChatIds = [TestChatId] };

        // act
        using var buffer = KvasSerializer.Default.Write(truncated);
        var bytes = buffer.WrittenMemory;
        var settings = KvasSerializer.Default.Read<UserPttSettings>(ref bytes);

        // assert
        settings.PttChatIds.Should().Equal(truncated.PttChatIds);
        settings.AreAudibleCuesEnabled.Should().BeFalse("`= true` does not survive a missing member");
        settings.HotWindow.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void WithPttChatIsIdempotent()
    {
        var joinedAt = Moment.EpochStart + TimeSpan.FromDays(1);
        var settings = new UserPttSettings()
            .WithPttChat(TestChatId, joinedAt)
            .WithPttChat(TestChatId, joinedAt);
        // act + assert
        settings.PttChats.Should().Equal(new PttChat(TestChatId, joinedAt));
        settings.PttChatIds.Should().Equal(TestChatId);
        settings.WithoutPttChat(TestChatId).PttChats.Should().BeEmpty();
        settings.WithoutPttChat(TestChatId).PttChatIds.Should().BeEmpty();
    }

    [Fact]
    public void WithPttChatEvictsTheOldestBeyondTheCap()
    {
        // arrange
        var t0 = Moment.EpochStart + TimeSpan.FromDays(1);
        var chatIds = Enumerable.Range(0, UserPttSettings.MaxChatCount + 1)
            .Select(i => ChatId.Parse($"evictionchat{i}"))
            .ToArray();
        var settings = new UserPttSettings();

        // act
        for (var i = 0; i < chatIds.Length; i++)
            settings = settings.WithPttChat(chatIds[i], t0 + TimeSpan.FromMinutes(i));

        // assert: the least-recently-joined entry (index 0) is gone, the rest survive in order
        settings.PttChats.Length.Should().Be(UserPttSettings.MaxChatCount);
        settings.PttChats.Select(c => c.ChatId).Should().Equal(chatIds.Skip(1));
        settings.PttChatIds.Should().Equal(chatIds.Skip(1));
    }

    [Fact]
    public void LegacyPttChatIdsSurfaceUnarmed()
    {
        // arrange: a blob written before PttChats existed
        var settings = new UserPttSettings { PttChatIds = [TestChatId] };
        var enabledAt = Moment.EpochStart + TimeSpan.FromDays(1);

        // act + assert: visible for listing/removal, but never armed under any live epoch
        settings.AllPttChats.Should().Equal(new PttChat(TestChatId, default));
        settings.IsArmedIn(TestChatId, enabledAt).Should().BeFalse();
        settings.WithoutPttChat(TestChatId).AllPttChats.Should().BeEmpty();
        settings.WithoutPttChat(TestChatId).PttChatIds.Should().BeEmpty();
    }

    [Fact]
    public void IsArmedRequiresConsentWithinTheEnableEpoch()
    {
        var enabledAt = Moment.EpochStart + TimeSpan.FromDays(1);
        // act + assert
        UserPttSettings.IsArmed(null, enabledAt + TimeSpan.FromHours(1)).Should().BeFalse();
        UserPttSettings.IsArmed(enabledAt, enabledAt - TimeSpan.FromSeconds(1)).Should().BeFalse();
        UserPttSettings.IsArmed(enabledAt, enabledAt).Should().BeTrue();
        UserPttSettings.IsArmed(enabledAt, enabledAt + TimeSpan.FromHours(1)).Should().BeTrue();
    }

    [Fact]
    public void PassesThroughAllSerializers()
    {
        var settings = new UserPttSettings {
            PttChatIds = [TestChatId],
            PttChats = [new PttChat(TestChatId, Moment.EpochStart + TimeSpan.FromDays(1))],
            IsFlipToTalkEnabled = false,
            ShakeSensitivity = ShakeSensitivity.High,
            AreGesturesAlwaysOn = true,
            HotWindow = TimeSpan.FromSeconds(120),
            AreAudibleCuesEnabled = false,
            IsHeadsetButtonEnabled = false,
            Origin = "test",
        };
        // act + assert
        AssertPassesThroughUnionSerializers(settings,
            (deserialized, original) => {
                var d = (UserPttSettings)deserialized;
                var o = (UserPttSettings)original;
                d.PttChatIds.Should().Equal(o.PttChatIds);
                d.PttChats.Should().Equal(o.PttChats);
                d.IsFlipToTalkEnabled.Should().Be(o.IsFlipToTalkEnabled);
                d.IsDoubleShakeEnabled.Should().Be(o.IsDoubleShakeEnabled);
                d.ShakeSensitivity.Should().Be(o.ShakeSensitivity);
                d.AreGesturesAlwaysOn.Should().Be(o.AreGesturesAlwaysOn);
                d.HotWindow.Should().Be(o.HotWindow);
                d.AreAudibleCuesEnabled.Should().Be(o.AreAudibleCuesEnabled);
                d.IsHeadsetButtonEnabled.Should().Be(o.IsHeadsetButtonEnabled);
            });
    }

    [Fact]
    public void UserAppSettingsFaceDownFlagPassesThroughAllSerializers()
    {
        // Inverted flag: null/absent means the face-down stop is ON for everyone by default.
        var settings = new UserAppSettings { IsFaceDownMicStopDisabled = true };
        // act + assert
        AssertPassesThroughUnionSerializers(settings,
            (deserialized, _) => ((UserAppSettings)deserialized).IsFaceDownMicStopDisabled.Should().BeTrue());
    }

    // Private methods

    private void AssertPassesThroughUnionSerializers<T>(T settings, Action<StoredSettings, StoredSettings> assertion)
        where T : StoredSettings
    {
        // StoredSettings has no JSON polymorphism config (only [MemoryPackUnion]/[Union] tags), so
        // AssertPassesThroughAllSerializers on the base-typed cast fails in the JSON passes for every
        // settings type, not just this one. Exercise the wire formats the union is actually
        // registered for, matching StoredSettingsSerializationTest.AssertBaseTypeRoundTrip.
        var msgp = ((StoredSettings)settings).PassThroughMessagePackByteSerializer(Out);
        assertion(msgp, settings);

        // MemoryPack is a legacy read path, so only settings predating the MessagePack write path
        // are [MemoryPackable]; the rest have no formatter to exercise.
        if (!SerializationCodeGen.IsMemoryPackable(typeof(T))) {
            Out.WriteLine($"Skipped MemoryPack: {typeof(T).Name} isn't [MemoryPackable].");
            return;
        }

        var mp = ((StoredSettings)settings).PassThroughMemoryPackByteSerializer(Out);
        assertion(mp, settings);
    }

    // Nested types

    // An exact copy of UserPttSettings as E2 shipped it - keys 0..7, no headset button.
    [DataContract, MessagePackObject]
    public sealed partial record E2UserPttSettings
    {
        [DataMember, Key(0)] public ChatId[] PttChatIds { get; init; } = [];
        [DataMember, Key(1)] public string Origin { get; init; } = "";
        [DataMember, Key(2)] public bool IsFlipToTalkEnabled { get; init; } = true;
        [DataMember, Key(3)] public bool IsDoubleShakeEnabled { get; init; } = true;
        [DataMember, Key(4)] public ShakeSensitivity ShakeSensitivity { get; init; }
        [DataMember, Key(5)] public bool AreGesturesAlwaysOn { get; init; }
        [DataMember, Key(6)] public TimeSpan HotWindow { get; init; } = TimeSpan.FromSeconds(60);
        [DataMember, Key(7)] public bool AreAudibleCuesEnabled { get; init; } = true;
    }

    // Stops before HotWindow and AreAudibleCuesEnabled, both of which have property initializers.
    [DataContract, MessagePackObject]
    public sealed partial record TruncatedUserPttSettings
    {
        [DataMember, Key(0)] public ChatId[] PttChatIds { get; init; } = [];
        [DataMember, Key(1)] public string Origin { get; init; } = "";
        [DataMember, Key(2)] public bool IsFlipToTalkEnabled { get; init; } = true;
        [DataMember, Key(3)] public bool IsDoubleShakeEnabled { get; init; } = true;
        [DataMember, Key(4)] public ShakeSensitivity ShakeSensitivity { get; init; }
        [DataMember, Key(5)] public bool AreGesturesAlwaysOn { get; init; }
    }
}
