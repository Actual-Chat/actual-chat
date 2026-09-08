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
        settings.AnswerWindow.Should().Be(TimeSpan.FromSeconds(15));
        settings.AreAudibleCuesEnabled.Should().BeTrue();
        (settings.IsHeadsetButtonEnabled ?? true).Should().BeTrue();
    }

    [Fact]
    public void HotWindowIsCappedAtOneMinute()
    {
        // arrange: a blob written when the UI still offered a 2-minute option
        var settings = new UserPttSettings { HotWindow = TimeSpan.FromSeconds(120) };

        // act + assert
        settings.HotWindow.Should().Be(TimeSpan.FromSeconds(60), "stored values above the cap must read as the cap");
        new UserPttSettings { HotWindow = TimeSpan.FromSeconds(30) }.HotWindow
            .Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ABlobPredatingTheAnswerWindowReadsAsTheDefault()
    {
        // arrange
        var e2Era = new E2UserPttSettings { PttChatIds = [TestChatId], Origin = "test" };

        // act
        using var buffer = KvasSerializer.Default.Write(e2Era);
        var bytes = buffer.WrittenMemory;
        var settings = KvasSerializer.Default.Read<UserPttSettings>(ref bytes);

        // assert: the member is absent from the blob, so it deserializes as zero - the getter
        // must normalize that to the default rather than hand a zero window to the policy.
        settings.AnswerWindow.Should().Be(TimeSpan.FromSeconds(15));
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
    public void WithPttChatMutedMutesOnlyUntilTheDeadline()
    {
        // arrange
        var joinedAt = Moment.EpochStart + TimeSpan.FromDays(1);
        var mutedAt = joinedAt + TimeSpan.FromHours(1);
        var mutedUntil = mutedAt + TimeSpan.FromMinutes(15);
        var settings = new UserPttSettings().WithPttChat(TestChatId, joinedAt);

        // act
        var muted = settings.WithPttChatMuted(TestChatId, mutedAt, mutedUntil);

        // assert
        settings.IsMutedIn(TestChatId, mutedAt).Should().BeFalse("nothing is muted by default");
        muted.PttChats.Should().Equal(new PttChat(TestChatId, joinedAt, mutedAt, mutedUntil));
        muted.IsMutedIn(TestChatId, mutedAt).Should().BeTrue();
        muted.IsMutedIn(TestChatId, mutedUntil - TimeSpan.FromSeconds(1)).Should().BeTrue();
        muted.IsMutedIn(TestChatId, mutedUntil).Should().BeFalse("the mute lapses at the deadline");
        muted.IsMutedIn(ChatId.Parse("someotherchat"), mutedAt).Should().BeFalse();
    }

    [Fact]
    public void WithPttChatMutedIgnoresAChatWithoutConsent()
    {
        var now = Moment.EpochStart + TimeSpan.FromDays(1);
        // act + assert
        new UserPttSettings().WithPttChatMuted(TestChatId, now, now + TimeSpan.FromHours(1))
            .PttChats.Should().BeEmpty("muting must never create consent");
    }

    [Fact]
    public void UnmutingAndReArmingClearTheMute()
    {
        // arrange
        var joinedAt = Moment.EpochStart + TimeSpan.FromDays(1);
        var mutedAt = joinedAt + TimeSpan.FromHours(1);
        var muted = new UserPttSettings()
            .WithPttChat(TestChatId, joinedAt)
            .WithPttChatMuted(TestChatId, mutedAt, mutedAt + TimeSpan.FromHours(8));

        // act + assert
        muted.WithPttChatUnmuted(TestChatId).PttChats.Should().ContainSingle()
            .Which.Should().Be(new PttChat(TestChatId, joinedAt), "unmuting keeps the consent as it was");
        muted.WithPttChat(TestChatId, mutedAt).PttChats.Should().ContainSingle()
            .Which.Should().Be(new PttChat(TestChatId, mutedAt), "re-arming replaces the entry, mute included");
    }

    [Fact]
    public void HushDefaultsAreSafe()
    {
        var settings = new UserPttSettings();
        // act + assert
        settings.HushDuration.Should().Be(TimeSpan.FromMinutes(15), "a blob predating the member reads zero");
        (settings.IsHushGestureEnabled ?? true).Should().BeTrue();
    }

    [Fact]
    public void WithAllPttChatsMutedMutesEveryConsentedChatButKeepsALaterDeadline()
    {
        // arrange
        var chatA = ChatId.Parse("hushchataaaaaaaaaaaa");
        var chatB = ChatId.Parse("hushchatbbbbbbbbbbbb");
        var joinedAt = Moment.EpochStart + TimeSpan.FromDays(1);
        var now = joinedAt + TimeSpan.FromHours(1);
        var settings = new UserPttSettings()
            .WithPttChat(chatA, joinedAt)
            .WithPttChat(chatB, joinedAt)
            .WithPttChatMuted(chatB, now - TimeSpan.FromMinutes(5), now + TimeSpan.FromHours(8));

        // act
        var hushed = settings.WithAllPttChatsMuted(now, now + TimeSpan.FromMinutes(15));

        // assert
        hushed.IsMutedIn(chatA, now).Should().BeTrue();
        hushed.PttChats.Single(c => c.ChatId == chatA).MutedUntil.Should().Be(now + TimeSpan.FromMinutes(15));
        hushed.PttChats.Single(c => c.ChatId == chatB).MutedUntil
            .Should().Be(now + TimeSpan.FromHours(8), "a hush never shortens a longer mute");
    }

    [Fact]
    public void WithPttChatsUnmutedClearsOnlyTheGivenChats()
    {
        // arrange
        var chatA = ChatId.Parse("hushchataaaaaaaaaaaa");
        var chatB = ChatId.Parse("hushchatbbbbbbbbbbbb");
        var joinedAt = Moment.EpochStart + TimeSpan.FromDays(1);
        var now = joinedAt + TimeSpan.FromHours(1);
        var settings = new UserPttSettings()
            .WithPttChat(chatA, joinedAt)
            .WithPttChat(chatB, joinedAt)
            .WithAllPttChatsMuted(now, now + TimeSpan.FromMinutes(15));

        // act
        var unmuted = settings.WithPttChatsUnmuted([chatA]);

        // assert
        unmuted.IsMutedIn(chatA, now).Should().BeFalse();
        unmuted.IsMutedIn(chatB, now).Should().BeTrue();
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
            PttChats = [
                new PttChat(
                    TestChatId,
                    Moment.EpochStart + TimeSpan.FromDays(1),
                    Moment.EpochStart + TimeSpan.FromDays(2),
                    Moment.EpochStart + TimeSpan.FromDays(2) + TimeSpan.FromHours(8)),
            ],
            IsFlipToTalkEnabled = false,
            ShakeSensitivity = ShakeSensitivity.High,
            AreGesturesAlwaysOn = true,
            HotWindow = TimeSpan.FromSeconds(30),
            AnswerWindow = TimeSpan.FromSeconds(30),
            AreAudibleCuesEnabled = false,
            IsHeadsetButtonEnabled = false,
            HushDuration = TimeSpan.FromHours(1),
            IsHushGestureEnabled = false,
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
                d.AnswerWindow.Should().Be(o.AnswerWindow);
                d.AreAudibleCuesEnabled.Should().Be(o.AreAudibleCuesEnabled);
                d.IsHeadsetButtonEnabled.Should().Be(o.IsHeadsetButtonEnabled);
                d.HushDuration.Should().Be(o.HushDuration);
                d.IsHushGestureEnabled.Should().Be(o.IsHushGestureEnabled);
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
