namespace ActualChat.Users.UnitTests;

public class UserModelSerializationTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void Avatar_Basic()
    {
        var avatar = new Avatar("avatar-1", 1) {
            Name = "Test Avatar",
            Bio = "A bio",
            AvatarKey = "key-1",
            PictureUrl = "https://example.com/pic.jpg",
        };

        var s = avatar.PassThroughSerializers(Out);
        s.Id.Should().Be(avatar.Id);
        s.Version.Should().Be(avatar.Version);
        s.Name.Should().Be(avatar.Name);
        s.Bio.Should().Be(avatar.Bio);
        s.AvatarKey.Should().Be(avatar.AvatarKey);
        s.PictureUrl.Should().Be(avatar.PictureUrl);
    }

    [Fact]
    public void AvatarFull_Basic()
    {
        var userId = UserId.New();
        var avatar = new AvatarFull(userId, "avatar-1", 1) {
            Name = "Test Avatar",
            IsAnonymous = true,
        };

        var s = avatar.PassThroughSerializers(Out);
        s.UserId.Should().Be(avatar.UserId);
        s.IsAnonymous.Should().Be(avatar.IsAnonymous);
        s.Name.Should().Be(avatar.Name);
    }

    [Fact]
    public void ChatPosition_Basic()
    {
        var pos = new ChatPosition(42, "origin");
        pos.AssertPassesThroughSerializers();
    }

    [Fact]
    public void GuestIdOption_Basic()
    {
        var guestId = UserId.NewGuest();
        var option = new GuestIdOption(guestId);
        option.AssertPassesThroughSerializers();
    }

    [Fact]
    public void UserAppSettings_Basic()
    {
        var settings = new UserAppSettings {
            Origin = "https://actual.chat",
            IsDataCollectionEnabled = true,
            AreExperimentalFeaturesEnabled = false,
            IsIncompleteUIEnabled = null,
            IsVideoDiagnosticsEnabled = true,
            IsAudioDiagnosticsEnabled = false,
        };
        settings.AssertPassesThroughSerializers();
    }

    [Fact]
    public void UserAvatarSettings_Basic()
    {
        var settings = new UserAvatarSettings {
            AvatarIds = ["avatar-1", "avatar-2"],
            DefaultAvatarId = "avatar-1",
        };
        settings.AssertPassesThroughSerializers(v => {
            v.AvatarIds.Should().Equal(settings.AvatarIds);
            v.DefaultAvatarId.Should().Be(settings.DefaultAvatarId);
        }, Out);
    }

    [Fact]
    public void UserBubbleSettings_Basic()
    {
        var settings = new UserBubbleSettings {
            ReadBubbles = ["bubble-1", "bubble-2"],
            Origin = "https://actual.chat",
        };
        settings.AssertPassesThroughSerializers(v => {
            v.ReadBubbles.Should().Equal(settings.ReadBubbles);
            v.Origin.Should().Be(settings.Origin);
        }, Out);
    }

    [Fact]
    public void ChatUserSettings_Basic()
    {
        var settings = new Chat.ChatUserSettings {
            Language = Languages.English,
            NotificationMode = ChatNotificationMode.ImportantOnly,
            VoiceMode = VoiceMode.TextAndVoice,
        };
        settings.AssertPassesThroughSerializers();
    }

    [Fact]
    public void UserChatRecordingDetectedLanguage_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var settings = new UserChatRecordingDetectedLanguage {
            Timestamp = new Moment(DateTime.UtcNow),
            ChatId = chatId,
            Language = Languages.English,
        };
        settings.AssertPassesThroughSerializers();
    }

    [Fact]
    public void UserEmailsSettings_Basic()
    {
        var settings = new UserEmailsSettings {
            Origin = "https://actual.chat",
            DigestTime = new TimeSpan(9, 0, 0),
            IsDigestEnabled = true,
        };
        settings.AssertPassesThroughSerializers();
    }

    [Fact]
    public void UserLanguageSettings_Basic()
    {
        var settings = new UserLanguageSettings {
            Primary = Languages.English,
            Secondary = Languages.Russian,
            Tertiary = Languages.German,
            Origin = "https://actual.chat",
        };
        settings.AssertPassesThroughSerializers();
    }

    [Fact]
    public void UserListeningSettings_Basic()
    {
        var settings = new UserListeningSettings {
            ListeningLinger = ListeningLinger.For30Seconds,
            Origin = "https://actual.chat",
        };
        var s = settings.PassThroughSerializers(Out);
        s.ListeningLinger.Should().Be(ListeningLinger.For30Seconds);
        s.Origin.Should().Be(settings.Origin);
    }

    [Fact]
    public void UserNavbarSettings_Basic()
    {
        var chatId = ChatId.Parse("the-actual-one");
        var placeId = PlaceId.New();
        var settings = new UserNavbarSettings {
            Origin = "https://actual.chat",
            PinnedChats = [chatId],
            PlacesOrder = [placeId],
        };
        var s = settings.PassThroughSerializers(Out);
        s.Origin.Should().Be(settings.Origin);
        s.PinnedChats.Length.Should().Be(1);
        s.PinnedChats[0].Should().Be(chatId);
        s.PlacesOrder.Length.Should().Be(1);
    }

    [Fact]
    public void UserOnboardingSettings_Basic()
    {
        var settings = new UserOnboardingSettings {
            IsAvatarStepCompleted = true,
            IsCreateChatsStepCompleted = true,
            IsVerifyPhoneStepCompleted = false,
            IsVerifyEmailStepCompleted = false,
            Origin = "https://actual.chat",
        };
        settings.AssertPassesThroughSerializers();
    }

    [Fact]
    public void UserTranscriptionEngineSettings_Basic()
    {
        var settings = new UserTranscriptionEngineSettings {
            Origin = "https://actual.chat",
        };
        settings.AssertPassesThroughSerializers();
    }

    [Fact]
    public void LocalAppSettings_Basic()
    {
        var settings = new LocalAppSettings {
            IsLogViewerEnabled = true,
            LocationAccuracy = GeoTrackingAccuracy.Low,
        };
        settings.AssertPassesThroughSerializers(AssertLocalAppSettingsEqual);
    }

    [Fact]
    public void LocalAppSettings_Default()
    {
        var settings = new LocalAppSettings();
        settings.AssertPassesThroughSerializers(AssertLocalAppSettingsEqual);
    }

    [Fact]
    public void LocalAppSettings_LocationAccuracyCanBeChanged()
    {
        var settings = new LocalAppSettings();
        settings.LocationAccuracy.Should().BeNull();
        settings.LocationAccuracyOrDefault.Should().Be(GeoTrackingAccuracy.Balanced);

        var updated = settings with { LocationAccuracy = GeoTrackingAccuracy.High };
        updated.LocationAccuracy.Should().Be(GeoTrackingAccuracy.High);
        updated.LocationAccuracyOrDefault.Should().Be(GeoTrackingAccuracy.High);
    }

    [Fact]
    public void LocalAppSettings_WithCameraMirrorOverrides()
    {
        var settings = new LocalAppSettings {
            CameraMirrorOverrides = new ApiMap<string, bool>()
                .With("cam-1", true)
                .With("cam-2", false),
        };
        settings.AssertPassesThroughSerializers(AssertLocalAppSettingsEqual);
    }

    private static void AssertLocalAppSettingsEqual(LocalAppSettings actual, LocalAppSettings expected)
    {
        actual.IsLogViewerEnabled.Should().Be(expected.IsLogViewerEnabled);
        actual.SelectedCameraDeviceId.Should().Be(expected.SelectedCameraDeviceId);
        actual.IsBackgroundBlurEnabled.Should().Be(expected.IsBackgroundBlurEnabled);
        actual.LocationAccuracy.Should().Be(expected.LocationAccuracy);
        actual.CameraMirrorOverrides.Should().BeEquivalentTo(expected.CameraMirrorOverrides);
    }
}
