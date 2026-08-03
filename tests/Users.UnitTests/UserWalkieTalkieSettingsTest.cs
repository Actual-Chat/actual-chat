namespace ActualChat.Users.UnitTests;

public class UserWalkieTalkieSettingsTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly ChatId TestChatId = ChatId.Parse("the-actual-one");

    [Fact]
    public void Defaults_AreSafe()
    {
        var settings = new UserWalkieTalkieSettings();
        settings.PttChatIds.Should().BeEmpty();
        settings.IsFlipToTalkEnabled.Should().BeTrue();
        settings.IsDoubleShakeEnabled.Should().BeTrue();
        settings.ShakeSensitivity.Should().Be(ShakeSensitivity.Medium);
        settings.AreGesturesAlwaysOn.Should().BeFalse();
        settings.HotWindow.Should().Be(TimeSpan.FromSeconds(60));
        settings.AreAudibleCuesEnabled.Should().BeTrue();
        settings.IsHeadsetButtonEnabled.Should().BeTrue();
    }

    [Fact]
    public void WithPttChat_IsIdempotent()
    {
        var settings = new UserWalkieTalkieSettings().WithPttChat(TestChatId).WithPttChat(TestChatId);
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
    public void UserAppSettings_FaceDownFlag_PassesThroughAllSerializers()
    {
        var settings = new UserAppSettings { IsFaceDownMicStopEnabled = true };
        AssertPassesThroughUnionSerializers(settings,
            (deserialized, _) => ((UserAppSettings)deserialized).IsFaceDownMicStopEnabled.Should().BeTrue());
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
}
