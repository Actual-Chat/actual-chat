using ActualChat.Kvas;

namespace ActualChat.Users.UnitTests;

public partial class UserWalkieTalkieSettingsTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");
    private static readonly KvasSerializer MemoryPackKvasSerializer = new() { PreferMemoryPack = true };

    [Fact]
    public void DefaultsAreSafe()
    {
        var settings = new UserWalkieTalkieSettings();
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
        var e2Era = new E2UserWalkieTalkieSettings {
            PttChatIds = [TestChatId],
            Origin = "test",
            AreAudibleCuesEnabled = false,
        };

        // act
        using var buffer = MemoryPackKvasSerializer.Write(e2Era);
        var bytes = buffer.WrittenMemory;
        var settings = MemoryPackKvasSerializer.Read<UserWalkieTalkieSettings>(ref bytes);

        // assert
        settings.PttChatIds.Should().Equal(e2Era.PttChatIds);
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
        var truncated = new TruncatedUserWalkieTalkieSettings { PttChatIds = [TestChatId] };

        // act
        using var buffer = MemoryPackKvasSerializer.Write(truncated);
        var bytes = buffer.WrittenMemory;
        var settings = MemoryPackKvasSerializer.Read<UserWalkieTalkieSettings>(ref bytes);

        // assert
        settings.PttChatIds.Should().Equal(truncated.PttChatIds);
        settings.AreAudibleCuesEnabled.Should().BeFalse("`= true` does not survive a missing member");
        settings.HotWindow.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void WithPttChatIsIdempotent()
    {
        var settings = new UserWalkieTalkieSettings().WithPttChat(TestChatId).WithPttChat(TestChatId);
        // act + assert
        settings.PttChatIds.Should().Equal(TestChatId);
        settings.WithoutPttChat(TestChatId).PttChatIds.Should().BeEmpty();
    }

    [Fact]
    public void PassesThroughAllSerializers()
    {
        var settings = new UserWalkieTalkieSettings {
            PttChatIds = [TestChatId],
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
                var d = (UserWalkieTalkieSettings)deserialized;
                var o = (UserWalkieTalkieSettings)original;
                d.PttChatIds.Should().Equal(o.PttChatIds);
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

    private void AssertPassesThroughUnionSerializers<T>(T settings, Action<StoredSettings, StoredSettings> assertion)
        where T : StoredSettings
    {
        // StoredSettings has no JSON polymorphism config (only [MemoryPackUnion]/[Union] tags), so
        // AssertPassesThroughAllSerializers on the base-typed cast fails in the JSON passes for every
        // settings type, not just this one. Exercise the two wire formats the union is actually
        // registered for, matching StoredSettingsSerializationTest.AssertBaseTypeRoundTrip.
        var mp = ((StoredSettings)settings).PassThroughMemoryPackByteSerializer(Out);
        assertion(mp, settings);
        var msgp = ((StoredSettings)settings).PassThroughMessagePackByteSerializer(Out);
        assertion(msgp, settings);
    }

    // Nested types

    // An exact copy of UserWalkieTalkieSettings as E2 shipped it - orders 0..7, no headset button.
    [DataContract, MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial record E2UserWalkieTalkieSettings
    {
        [DataMember, MemoryPackOrder(0)] public ChatId[] PttChatIds { get; init; } = [];
        [DataMember, MemoryPackOrder(1)] public string Origin { get; init; } = "";
        [DataMember, MemoryPackOrder(2)] public bool IsFlipToTalkEnabled { get; init; } = true;
        [DataMember, MemoryPackOrder(3)] public bool IsDoubleShakeEnabled { get; init; } = true;
        [DataMember, MemoryPackOrder(4)] public ShakeSensitivity ShakeSensitivity { get; init; }
        [DataMember, MemoryPackOrder(5)] public bool AreGesturesAlwaysOn { get; init; }
        [DataMember, MemoryPackOrder(6)] public TimeSpan HotWindow { get; init; } = TimeSpan.FromSeconds(60);
        [DataMember, MemoryPackOrder(7)] public bool AreAudibleCuesEnabled { get; init; } = true;
    }

    // Stops before HotWindow and AreAudibleCuesEnabled, both of which have property initializers.
    [DataContract, MemoryPackable(GenerateType.VersionTolerant)]
    public sealed partial record TruncatedUserWalkieTalkieSettings
    {
        [DataMember, MemoryPackOrder(0)] public ChatId[] PttChatIds { get; init; } = [];
        [DataMember, MemoryPackOrder(1)] public string Origin { get; init; } = "";
        [DataMember, MemoryPackOrder(2)] public bool IsFlipToTalkEnabled { get; init; } = true;
        [DataMember, MemoryPackOrder(3)] public bool IsDoubleShakeEnabled { get; init; } = true;
        [DataMember, MemoryPackOrder(4)] public ShakeSensitivity ShakeSensitivity { get; init; }
        [DataMember, MemoryPackOrder(5)] public bool AreGesturesAlwaysOn { get; init; }
    }
}
